# Implementation Plan: The kiosk and the wall display on one origin

**Branch**: `494-kiosk-wall-single-origin` | **Date**: 2026-11-12 (revised at the Plan gate, same day) | **Spec**: [spec.md](./spec.md)

**Input**: Feature specification from `specs/494-kiosk-wall-single-origin/spec.md` (clarified: Q1 option A; Q2–Q4 answered by the owner at
the Plan gate). **Governing decision**: ADR-0192 (accepted). **Issue**: #3246 (phase 2 of epic #3248). **Builds on**: spec 491 (merged,
#3253). **Lands after**: spec 493 (#3259, owner).

## Summary

The kiosk and the wall display become paths on the gateway's origin, `/kiosk/` and `/wall/`, in the gateway's dev proxy mode and its static
mode, and both instances are **required** gateway configuration whenever the UI is hosted. Each instance runs under a Vite `base` derived
from the mode flag it already has; the router, the OIDC callback and the sign-in return follow that base. The layout hub is reached
**directly on `layout-composition`, cross-origin**, admitted by a new CORS policy scoped to the hub path; the kiosk learns the absolute hub
URL from the existing `VITE_LAYOUT_HUB_URL`, set by the AppHost. REST is unchanged (absolute gateway origin, Q1). Two spikes decided the
risky front-end parts before anything depends on them (research R1, R2).

§IV legs touched: **none gains a hop**. The event → overlay leg loses one intermediary in dev (the Vite `/hubs` proxy) and gains a CORS
preflight at connection set-up only, never per message (research R2). `BoundaryTests` is not edited.

## Technical Context

**Language/Version**: C# on .NET 10 (gateway, `layout-composition`, AppHost); TypeScript, React 19, Vite **8.3.6**.

**Primary Dependencies**: `Yarp.ReverseProxy` 2.3.0 and ASP.NET Core static files (already in `UiHosting`); ASP.NET Core CORS (shared
framework, already referenced by `LayoutComposition.Infrastructure`); `react-router-dom` (`basename`); `react-oidc-context`/`oidc-client-ts`
and `@microsoft/signalr` 10.0.11 **unchanged**. No new package.

**Storage**: N/A.

**Testing**: xUnit + Shouldly in `Integration.Tests` (in-process gateway harness `GatewayTestHost`/`UiProxyStack`/`StaticUiStack`; an
in-process host for the hub CORS wiring; AppHost model tests; `[Trait("Category", "FixtureLogic")]`; `ci-shards/shard-2.filter`); vitest
(`apps/kiosk-web`, `apps/shared`); `Architecture.Tests`; Playwright `kiosk` and `wall` projects in CI (characterisation, and now the
full-stack evidence for the cross-origin hub).

**Target Platform**: the gateway and `layout-composition` on Linux/Windows; evergreen Chromium on kiosk devices.

**Project Type**: web application (gateway, one bounded-context service, AppHost, one Vite app run as two instances).

**Performance Goals**: no new budget. Kiosk click-to-first-frame through `/kiosk/` not worse than through `:5174` beyond noise (SC-006),
measured twice. One hub preflight per connection, none per push (research R2).

**Constraints**: hub and WHEP never through the gateway; no added hop; `@microsoft/signalr` and the hub class untouched; spec 491's gateway
**assertions** unmodified (fixtures may gain the new required settings, owner); kiosk suites unmodified; one gateway instance (ADR-0153).

**Scale/Scope**: two UI prefixes, four required gateway settings, one CORS policy and one setting on `layout-composition`, one Vite config,
three kiosk source files, one shared plugin, the AppHost web wiring.

## Constitution Check

_GATE: passes before research; re-checked after the Plan-gate revision (still passes)._

| Rule                               | Status                                                                                                                                                                                                                                                                                                                                                                         |
| ---------------------------------- | ------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------ |
| §IV latency budget                 | **Event → overlay state**: browser → `layout-composition` direct; one intermediary fewer than today in dev, none added in production; CORS evaluates the hub's HTTP requests (negotiate, reconnect, long-polling fallback with a cached preflight), never the pushed frames on the WebSocket. **SFU → decode**: WHEP unchanged. Other legs: not touched. Shown by W12 (below). |
| §IV structural guard               | `ApiGateway_does_not_sit_on_the_realtime_or_media_latency_legs` unedited; the gateway gains plain YARP routes only.                                                                                                                                                                                                                                                            |
| §VI Aspire is the composition root | The gateway's kiosk/wall destinations, the kiosk's hub URL and `layout-composition`'s allowed origins are declared in `WebApplications.cs` from endpoint references; no literal port, no hand-wired URL.                                                                                                                                                                       |
| §VIII safe at trust boundaries     | The hub's CORS policy echoes only configured origins (credentials require it), refuses `*` at start, and is scoped to the hub path; it relaxes no authorization (the hub keeps `[Authorize]` and its scope policy; a preflight carries no token and grants none). The sign-in return is narrowed to the instance's base. No realm or scope change.                             |
| §III bounded-context isolation     | `layout-composition` gains no reference; the gateway references no context.                                                                                                                                                                                                                                                                                                    |
| ADR-0051 / ADR-0070                | The CORS wiring is a pair of builder/app extensions beside the hub (`Add…`/`Use…`), called from the minimal-API `Program.cs`; it mirrors the gateway's `AddGatewayCors`.                                                                                                                                                                                                       |
| ADR-0105 guards                    | Argument guards with `Ensure.That`; the configuration refusal (`*`) is a startup `InvalidOperationException` naming the key, as `UiHosting` does for its settings.                                                                                                                                                                                                             |
| ADR-0036 smallest change           | No new setting for the base; one env value for the hub URL; REST unchanged; the gateway fallback reused from 493; the Vite `/hubs` proxy left in place rather than removed in the same slice.                                                                                                                                                                                  |
| ADR-0139 test colour               | Every work package below is declared red-first or characterisation; the `layout-composition` change is backend code on the push service and is red-first.                                                                                                                                                                                                                      |
| ADR-0193 frontend imports          | New TS modules: extensionless relative imports, named exports.                                                                                                                                                                                                                                                                                                                 |
| ADR-0030 / ADR-0086                | Conventional commits, no `Co-Authored-By`.                                                                                                                                                                                                                                                                                                                                     |

No violations; Complexity Tracking is empty.

## Decisions

Each is expanded, with evidence and alternatives, in [research.md](./research.md).

- **D1. Base from the mode flag, read once.** `vite.config.ts` sets `base` to `/wall/` when `VITE_KIOSK_MODE` is `wall`, else `/kiosk/`
  (a pure `kioskBase(mode)`; `process.env` merged with `loadEnv`). The app reads only `import.meta.env.BASE_URL`. **Vitest keeps base `/`**
  so the 39 existing test files run in the environment they were written for (R4).
- **D2. Router basename and OIDC paths derive from the base.** `createBrowserRouter(routes, { basename })`; `redirect_uri` = origin + base
  - `oidc/callback`; the sign-in return accepts only a path under the base, else the base. Small pure functions taking the base (R4).
- **D3. Hub: a CORS policy on `layout-composition`, and the kiosk calls it directly** (owner, Q2). `LayoutHubCors`
  (`src/LayoutComposition/Infrastructure/Broadcasting/`) registers a named policy from `LayoutHub:Cors:AllowedOrigins` (refusing any entry
  containing `*`) with credentials, the headers `Authorization`, `X-Requested-With`, `X-SignalR-User-Agent`, `Content-Type`, the methods
  `GET`, `POST`, `DELETE`, and a 10-minute preflight max-age; it is mounted with `UseWhen` for the hub path only, **before**
  `UseAuthentication`. `Program.cs` gains the two calls; its `MapHub` line is not touched. The kiosk gets the absolute URL through the
  existing `VITE_LAYOUT_HUB_URL` (AppHost: `<layout-composition endpoint>/hubs/layouts` on both instances). The Vite `/hubs` proxy stays as
  a fallback for Vite started outside Aspire (R2, R7, R8).
- **D4. Gateway: two required instances per mode** (owner, Q4). `Ui:Proxy:Kiosk`/`Wall` add YARP routes `/kiosk/{**catch-all}`,
  `/wall/{**catch-all}` (no rate-limit or CORS policy, ahead of the shell) and are validated by the existing `RequiredAddress`;
  `Ui:Static:KioskRoot`/`WallRoot` add static files at `/kiosk`, `/wall` and are validated by the existing `StaticRoots.Root` with entry
  file `index.html`. Missing → the gateway does not start, naming the key (R5).
- **D5. The fallback is 493's, generalised.** After 493 merges, its shell fallback (navigation-aware dotted rule, `no-cache`, `nosniff`) is
  extracted into one helper taking a prefix and an `index.html`, under 493's own tests; the kiosk and wall use it, so their `index.html`
  gets `no-cache` too (R5).
- **D6. The gateway redirects `/kiosk` and `/wall`** to the slashed paths in both modes (Vite 404s them, R1).
- **D7. The dev font preload carries the base.** `fontPreload.ts` prefixes `config.base` in `serve`; byte-identical for base `/` (R1).
- **D8. Realm: observe at Verify; if the token exchange fails at `:5180`, file a separate issue against spec 491 and all apps** (owner, Q3).
  No realm edit in this spec (R3).
- **D9. Test placement.** New behaviour gets new classes: `GatewayKioskUiProxyTests`, `GatewayKioskUiStaticTests`, `LayoutHubCorsTests`,
  `AppHostKioskOneOriginTests`. 493's and 491's classes keep their assertions; only 491's fixtures change (W2).
- **D10. AppHost allowed origins come from endpoint references** inside `AddWebApplications`: `kiosk-web`, `kiosk-wall` and the gateway
  (the one origin). Lanes without the web apps (the integration fixture) leave the list empty, which is today's behaviour (R8).

## Work packages and test colour

Phase 3 turns these into tasks. **Red** = a test observed failing before the change, failure quoted in the PR (ADR-0139).
**Characterisation** = captured green before, passes with its assertions unmodified after.

| #   | Work package                                                                                                                                                                                                                                                                              | Colour                                                                                                                 | Files                                                                                                                                                                                               | Depends on                 |
| --- | ----------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------- | ---------------------------------------------------------------------------------------------------------------------- | --------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------- | -------------------------- |
| W0  | Baseline: kiosk-web and shared vitest counts (one at a time), `Architecture.Tests`, spec 491 + 493 gateway suites, the `layout-composition` push integration tests (in CI)                                                                                                                | Characterisation (recorded)                                                                                            | `verification.md`                                                                                                                                                                                   | 493 merged, branch rebased |
| W1  | Extract 493's shell fallback into a prefix-taking helper                                                                                                                                                                                                                                  | **Characterisation** (493's tests unmodified)                                                                          | `src/ApiGateway/UiHosting.cs`                                                                                                                                                                       | W0                         |
| W2  | **Fixture-only commit**: spec 491's gateway fixtures supply `Ui:Proxy:Kiosk`/`Wall` (stub destinations) and `Ui:Static:KioskRoot`/`WallRoot` (temp roots with `index.html`). No assertion touched; the suites stay green before W3 lands.                                                 | **Characterisation** (assertions unmodified; owner-approved fixture edit)                                              | `tests/Integration.Tests/ApiGateway/UiProxyStack.cs`, the `StaticUiStack` in `GatewayUiStaticTests.cs`                                                                                              | W1                         |
| W3  | Proxy mode: `/kiosk/**`, `/wall/**` routes, required destinations (start refused, naming each key), precedence over the shell, no policies, HMR upgrade, `/kiosk`→`/kiosk/`                                                                                                               | **Red**                                                                                                                | `UiHosting.cs`; new `GatewayKioskUiProxyTests.cs`                                                                                                                                                   | W2                         |
| W4  | Static mode: required roots (start refused on missing key, missing directory, no `index.html`), per-prefix fallback via W1's helper, `no-cache` on the instances' `index.html`, missing file → 404, redirect                                                                              | **Red**                                                                                                                | `UiHosting.cs`; new `GatewayKioskUiStaticTests.cs`                                                                                                                                                  | W2 (W3 for the redirect)   |
| W5  | `layout-composition` hub CORS: allowed origin → `Access-Control-Allow-Origin` echoed + `Allow-Credentials` + the four headers + the three methods + `Max-Age`; disallowed origin → none; a REST path → none; `*` in config → start refused                                                | **Red** (in-process host over `LayoutHubCors` with a stand-in endpoint at `LayoutLifecycleHub.Path` and one REST path) | new `src/LayoutComposition/Infrastructure/Broadcasting/LayoutHubCors.cs`; `src/LayoutComposition/Api/Program.cs` (two calls); new `tests/Integration.Tests/LayoutComposition/LayoutHubCorsTests.cs` | W0                         |
| W6  | Base from the mode flag (`kioskBase`), Vitest keeps `/`                                                                                                                                                                                                                                   | **Red** (unit test of `kioskBase`)                                                                                     | `apps/kiosk-web/vite.config.ts`, new `apps/kiosk-web/src/app/kioskBase.ts` + test                                                                                                                   | W0                         |
| W7  | Router basename                                                                                                                                                                                                                                                                           | **Red**                                                                                                                | `apps/kiosk-web/src/app/router.tsx`, new test                                                                                                                                                       | W6                         |
| W8  | `redirect_uri` and the sign-in return rule (`/`, `//x`, outside the base, missing, under the base)                                                                                                                                                                                        | **Red**                                                                                                                | `apps/kiosk-web/src/app/auth.ts`, new `src/app/signinPaths.ts` + test                                                                                                                               | W6                         |
| W9  | Dev font preload under the base                                                                                                                                                                                                                                                           | **Red** for base `/kiosk/`; **characterisation** for base `/`                                                          | `apps/shared/src/ui/fonts/fontPreload.ts`, new test beside it                                                                                                                                       | W0                         |
| W10 | AppHost: gateway `Ui__Proxy__Kiosk`/`Wall` (same commit as W3, or run mode cannot start); kiosk `VITE_LAYOUT_HUB_URL`; `layout-composition` `LayoutHub__Cors__AllowedOrigins__N` from the three endpoints                                                                                 | **Red** (AppHost model test); composition snapshots **move with the decision**, recorded                               | `src/AppHost/WebApplications.cs`; new `tests/Integration.Tests/AppHostKioskOneOriginTests.cs`; `AppHostComposition/*.snapshot.txt`                                                                  | W3, W5                     |
| W11 | Docs: `docs/deployment-frontend-env.md` (paths, base, the absolute hub URL, `LayoutHub:Cors:AllowedOrigins` for the deploy layer, no wildcard); 491's contract row points here                                                                                                            | n/a (docs)                                                                                                             | `docs/deployment-frontend-env.md`, `specs/491-…/contracts/gateway-paths.md` (one row)                                                                                                               | W3–W10                     |
| W12 | §IV evidence for the hub: no diff to the broadcaster, the hub class or their tests; `Max-Age` pinned (W5); push integration tests unmodified; at Verify, one `OPTIONS` per connection and none during a burst of pushes                                                                   | **Characterisation** + observation                                                                                     | `verification.md`                                                                                                                                                                                   | W5, W10                    |
| W13 | Characterisation run: kiosk-web, shared, `Architecture.Tests` (`BoundaryTests` no diff; `EndpointScopeDeclarationTests` register still matches `MapHub`), all gateway suites, Release build, `dotnet format`, `tsc`, `eslint`, `prettier`, shard coverage                                 | **Characterisation**                                                                                                   | none                                                                                                                                                                                                | W1–W12                     |
| W14 | Verify (needs the stack): sign-in at `/kiosk/` and `/wall/` (D8; file the issue if it fails), live overlay, hub on `layout-composition` and absent from the gateway log (SC-002), WHEP, zero 429 (SC-005), first-frame twice (SC-006), fixed ports; CI `kiosk`/`wall` projects unmodified | Observation                                                                                                            | `verification.md`                                                                                                                                                                                   | W13                        |

**Parallelism (ADR-0109):** W5 (backend, `layout-composition`) is independent of everything but W0 and can start at once. After W2, the
gateway track (W3 → W4) runs beside the kiosk track (W6 → W7, W8) and W9; they own disjoint files. W10 joins W3 and W5. W0 blocks
everything; W1 → W2 block the gateway track.

## Project Structure

### Documentation (this feature)

```text
specs/494-kiosk-wall-single-origin/
├── spec.md
├── plan.md               # this file
├── research.md           # R1–R8: spikes, evidence, alternatives
├── contracts/
│   └── kiosk-paths.md    # path map additions, Ui keys, the hub CORS setting, kiosk environment, application contract
├── quickstart.md         # how to see it working; what was spiked and what is left to Verify
├── checklists/requirements.md
└── tasks.md              # /speckit-tasks (not created here)
```

(No `data-model.md`: no stored entities.)

### Source Code (repository root)

```text
src/ApiGateway/UiHosting.cs                                         # W1, W3, W4
src/LayoutComposition/Infrastructure/Broadcasting/LayoutHubCors.cs  # new, W5
src/LayoutComposition/Api/Program.cs                                # two calls, W5 (MapHub line untouched)
src/AppHost/WebApplications.cs                                      # W10
apps/kiosk-web/vite.config.ts                                       # W6
apps/kiosk-web/src/app/kioskBase.ts                                 # new, W6
apps/kiosk-web/src/app/router.tsx                                   # W7
apps/kiosk-web/src/app/signinPaths.ts                               # new, W8
apps/kiosk-web/src/app/auth.ts                                      # W8
apps/shared/src/ui/fonts/fontPreload.ts                             # W9
docs/deployment-frontend-env.md                                     # W11
tests/Integration.Tests/ApiGateway/UiProxyStack.cs                  # W2, fixture only
tests/Integration.Tests/ApiGateway/GatewayUiStaticTests.cs          # W2, nested StaticUiStack fixture only
tests/Integration.Tests/ApiGateway/GatewayKioskUiProxyTests.cs      # new, W3
tests/Integration.Tests/ApiGateway/GatewayKioskUiStaticTests.cs     # new, W4
tests/Integration.Tests/LayoutComposition/LayoutHubCorsTests.cs     # new, W5
tests/Integration.Tests/AppHostKioskOneOriginTests.cs               # new, W10
tests/Integration.Tests/AppHostComposition/*.snapshot.txt           # move with W10, recorded
tests/Integration.Tests/ci-shards/shard-2.filter                    # the four new classes
```

**Structure Decision**: web application; a gateway hosting slice, one CORS policy on the hub's service, path configuration in the kiosk app.
No new project.

## What 494 rebases over once 493 merges

493's plan was not pushed when this plan was written; the list is from its spec (`origin/493-gateway-edge-cases`, `643fc2e6`) and is
re-checked against 493's merged diff at W0.

| 493 change                                                                                                                    | File                                               | What 494 does with it                                                                                                                                                 |
| ----------------------------------------------------------------------------------------------------------------------------- | -------------------------------------------------- | --------------------------------------------------------------------------------------------------------------------------------------------------------------------- |
| `MapShellFallback`: navigation-aware dotted rule; `Cache-Control: no-cache` on every `index.html` response                    | `src/ApiGateway/UiHosting.cs`                      | **Reuses it.** W1 extracts it into a prefix-taking helper (characterisation: 493's tests unmodified); kiosk and wall call it.                                         |
| Static-file options: `no-cache` on `remoteEntry.js` and a direct `index.html`                                                 | `src/ApiGateway/UiHosting.cs` (`StaticFiles`)      | **Reuses it** for the kiosk and wall roots (their `index.html`); no remote entry exists there.                                                                        |
| Any new required-key or fixture shape in `StaticUiStack`/`UiProxyStack`                                                       | `tests/Integration.Tests/ApiGateway/…`             | W2 edits the **merged** fixtures, not today's; if 493 added keys there, W2 adds beside them.                                                                          |
| `streams/authorize` carve-out for every method                                                                                | `src/ApiGateway/GatewayHost.cs`                    | Not touched by 494.                                                                                                                                                   |
| Dev proxy keys anchored, query string accepted                                                                                | `apps/management-web/src/app/devProxy.ts`          | Not touched by 494.                                                                                                                                                   |
| New/extended tests in `GatewayUiStaticTests`, `GatewayHostCharacterisationTests`, `GatewayApiPrefixTests`, `devProxy.test.ts` | `tests/…`, `apps/management-web/…`                 | Part of 494's characterisation run; 494's own cases go in separate classes (D9). W2's fixture edit in `GatewayUiStaticTests.cs` is the one shared file: rebase there. |
| Shard filter entries for any new 493 classes                                                                                  | `tests/Integration.Tests/ci-shards/shard-*.filter` | Likely textual conflict with 494's four entries; keep both.                                                                                                           |

If 493's merged fallback differs from its spec, D5 follows the merged code and the difference is recorded in `verification.md`.

## Risks and how each is bounded

| Risk                                                                                                    | Bound                                                                                                                                                                                     |
| ------------------------------------------------------------------------------------------------------- | ----------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------- |
| A wrong CORS policy silently breaks every kiosk's live updates (the badge goes "degraded")              | W5 red-first in-process; CI's `kiosk`/`wall` projects (live-update specs at `:5174`/`:5175`) now open the hub cross-origin and fail if it is wrong; Verify reads the network log.         |
| The policy's ordering lets `[Authorize]` refuse the preflight                                           | D3 mounts CORS before authentication; W5 asserts a token-less preflight gets 204 with the CORS headers.                                                                                   |
| CORS leaks onto `layout-composition`'s REST endpoints                                                   | Scoped by path (`UseWhen`); W5 asserts a REST path gets no CORS answer.                                                                                                                   |
| Long-polling fallback pre-flights per poll                                                              | `Access-Control-Max-Age` (10 min), pinned by W5.                                                                                                                                          |
| An AppHost cycle (`layout-composition` env references the kiosks, which reference and wait for it)      | Endpoint references only, no `WaitFor` from `layout-composition` (the gateway↔shell mutual reference is precedent); W10's model test and the run-mode boot at Verify are the observation. |
| Required gateway keys stop a gateway that was configured for spec 491 alone                             | Intended (owner, Q4); W10 lands the AppHost keys with W3; no deployment exists yet (ADR-0192 decision 10).                                                                                |
| Sign-in at `:5180` fails the token exchange's CORS (realm `"+"` with a wildcard port)                   | D8: observed at W14; a separate issue against 491/all apps if it fails.                                                                                                                   |
| The kiosk e2e suite meets an un-prefixed deep link on a fixed port                                      | Searched: none (R1). CI is the observation; a failure stops the PR, not an edited spec.                                                                                                   |
| Vitest picks up the new base                                                                            | D1: base stays `/` under Vitest; W13 counts equal W0 plus the new tests.                                                                                                                  |
| 493 lands a different fallback than its spec says                                                       | Rebase over the merged code; W1 is characterised by 493's own tests.                                                                                                                      |
| `/kiosk` redirect endpoint loses to the YARP `/kiosk/{**catch-all}` route (which also matches `/kiosk`) | W3 pins it red-first; if precedence goes the other way, the redirect gets an explicit order or the YARP match is narrowed.                                                                |

## Deviations from the spec

None remain open. The Plan-gate answers changed the spec itself (recorded there as Q2–Q4): FR-003 (required instances, start refused),
FR-007 (observe, file separately), FR-008 (CORS on `layout-composition`, direct hub), FR-016 (the hub service gains only the CORS answer),
the edge cases and the characterisation wording (spec 491 fixtures may change, assertions may not).

## Found while planning, not in scope

- **The console's wall-detail page opens the hub with the page-relative default** (`WallDetailPage.tsx`), and `management-web` has no
  `/hubs` dev proxy, so its live updates look unreachable in dev at `:5173` and `:5180`. Not observed. To be filed as its own issue; D3's
  policy plus `VITE_LAYOUT_HUB_URL` on `management-web` would fix it.

## Plan gate: owner-confirmed (2026-11-12)

The plan was approved at the Plan gate. The three open points were answered:

1. **Preflight max-age 10 minutes** (D3): accepted.
2. **The Vite `/hubs` proxy and `VITE_LAYOUT_HUB_ORIGIN` stay** as a fallback for Vite started outside Aspire; their removal is a
   follow-up, not this spec.
3. **The console's wall-detail hub defect** (above) is filed as a separate issue by the orchestrator; this spec does not file or fix it.
