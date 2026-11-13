# Implementation Plan: The operator console's live updates reach the hub

**Branch**: `495-console-hub-url` | **Date**: 2026-11-13 | **Spec**: [spec.md](spec.md)

**Input**: Feature specification from `specs/495-console-hub-url/spec.md`. **Issue**: #3263 (epic #3248). **Governing ADRs**: ADR-0192
decision 3 and ADR-0152, both **unchanged**. **Lane**: supervised (ADR-0037); no ADR is written.

## Summary

Give `management-web` the layout hub's address the way the kiosks already have it, and let `layout-composition` admit the console's
origin. Two AppHost lines, one corrected comment block, one documentation sentence, and tests. **No frontend source changes**: the page
already reads `VITE_LAYOUT_HUB_URL` through `hubUrl.ts`, and the policy it needs is the one spec 494 merged. The hub stays off the gateway
and the gateway's boundary test is not touched.

| Change                                                                                                                                  | Where                                                                                          |
| --------------------------------------------------------------------------------------------------------------------------------------- | ---------------------------------------------------------------------------------------------- |
| `.WithEnvironment("VITE_LAYOUT_HUB_URL", LayoutHubUrl(layoutComposition))` on `management-web`                                          | `src/AppHost/WebApplications.cs` (`AddManagementWeb`, which already takes `layoutComposition`) |
| `LayoutHub__Cors__AllowedOrigins__3` = `managementWeb.GetEndpoint("http")`                                                              | same file, the block after `WithOneOriginUi`                                                   |
| Correct the two comments that say the console has no hub address / the hub admits only the kiosks, the wall and the one origin (FR-008) | same file, lines ~14–27 and ~87                                                                |
| Say the AppHost now lists `management-web` among the admitted origins; record the production requirement for the console (FR-007)       | `docs/deployment-frontend-env.md`                                                              |

## Technical Context

**Language/Version**: C# on .NET 10 (AppHost); TypeScript is read, not changed.

**Primary Dependencies**: .NET Aspire (`EndpointReference`, `ReferenceExpression`); the existing `LayoutHubCors` policy.

**Storage**: N/A.

**Testing**: xUnit + Shouldly. The AppHost's wiring is tested structurally, without booting the stack, in
`tests/Integration.Tests/AppHostKioskOneOriginTests.cs` and `AppHostOneOriginTests.cs`; the CORS behaviour is already covered in
`LayoutComposition/LayoutHubCorsTests.cs`. One Playwright spec observes the page end to end.

**Target Platform**: the Aspire dev stack and CI; the production edge is out of scope (#1015).

**Project Type**: web application composition (AppHost + two React apps).

**Performance Goals**: none new; SC-001's 2 s is the page's re-read after a scene switch, observed once.

**Constraints**: §IV event-to-overlay path gains no hop; `ApiGateway_does_not_sit_on_the_realtime_or_media_latency_legs` unmodified.

**Scale/Scope**: one console resource, four allowed origins.

## Constitution Check

_GATE: Must pass before Phase 0 research. Re-checked after Phase 1 design._

| Principle                                 | Verdict     | Note                                                                                                                                    |
| ----------------------------------------- | ----------- | --------------------------------------------------------------------------------------------------------------------------------------- |
| I. On-prem first                          | Passes      | No new external dependency.                                                                                                             |
| II. Value objects                         | Not engaged | No domain model is touched.                                                                                                             |
| III. Bounded-context isolation            | Passes      | The AppHost already references `layout-composition` by resource, not by project; the architecture tests are unmodified.                 |
| IV. Latency budget                        | Passes      | **Legs touched: none.** The console opens the same direct connection the kiosks do. The spec's table states each leg.                   |
| V. Spec-driven                            | Passes      | Spec 495 → this plan → tasks; every task references #3263.                                                                              |
| VI. Aspire is the composition root        | Passes      | The URL and the origin are endpoint references in `AppHost`; no connection string is wired by hand.                                     |
| VII. Observability                        | Not engaged | No leg is added or moved, so no dashboard obligation attaches.                                                                          |
| VIII. Safe by default at trust boundaries | Passes      | The origin list stays exact; a wildcard is still refused at start; the one new entry is an endpoint reference, not a configured string. |
| Testing (§Testing, ADR-0139)              | Passes      | Behaviour-changing, so **red** (see Phase 4a below).                                                                                    |

No violations; Complexity Tracking is empty.

## Decisions

1. **Where the console's hub URL comes from: the AppHost, an endpoint reference, not a fixed port.** Aspire assigns `layout-composition`'s
   port per run (edge case 3), so the value must be a `ReferenceExpression`, exactly as `LayoutHubUrl(...)` already builds it for the kiosks.
   The same helper is reused; no second spelling of the hub path appears.
2. **No `VITE_LAYOUT_HUB_ORIGIN`, no `WithReference`, no `WaitFor` for the console.** `VITE_LAYOUT_HUB_ORIGIN` exists only to feed the
   kiosks' `/hubs` dev proxy, which the console does not have and, by the spec, must not gain. `WithReference` would add a service-discovery
   key with hyphens that POSIX shells drop (the note at `WebApplications.cs:26`). `WaitFor` would delay the shell's start for a page that
   tolerates the hub being down, and the console's own declaration records that same reasoning for its other references.
3. **The console's origin is added as an endpoint reference, index 3.** The list stays exact-origin and wildcard-free (FR-003). The
   gateway's origin, `:5180`, is already index 2, so the console at the one origin is already admitted; `:5173` is the addition. The order
   of the indices carries no meaning.
4. **No frontend change.** `resolveLayoutHubUrl` returns the configured value when it is set. The `/hubs/layouts` fallback is kept for a
   Vite server started outside Aspire (edge case 2): it stays harmless, and the production throw is unchanged.
5. **FR-009 (tell the operator when the hub is unreachable) is NOT in this slice.** It changes UI behaviour on a path other pages share
   (`useLayoutLifecycle`, the kiosk), it needs its own red test and its own review, and #3263 is about the connection not establishing. It
   becomes its own issue, filed when this PR opens; this plan names it so the omission is a decision, not a gap.
6. **The existing assertion that lists the admitted origins is edited by this feature, and that is the red test.**
   `Layout_composition_admits_the_kiosk_the_wall_and_the_gateway_origins_from_endpoint_references` currently expects
   `["api-gateway", "kiosk-wall", "kiosk-web"]`. This is a behaviour-changing feature, so the assertion moves first (to include
   `management-web`) and is observed failing; it is not a characterisation test, so the "an assertion that has to be edited is evidence the
   behaviour moved" rule does not block it, and the PR says so.
7. **The three AppHost composition snapshots change, and they are not "adjusted to green".** `Developer`, `EndToEndJob` and `Unparseable`
   `.snapshot.txt` record every resource's environment; the console's new variable and the new origin entry land in them. They are literals
   with **no regeneration path** (the test class says so), so they are **hand-edited first**, annotation indices included, observed red
   against the unedited AppHost, and green after it changes. The PR quotes both and says the composition moved on purpose. (Corrected from
   an earlier draft that said "regenerated"; there is nothing to regenerate with.)

## Phase 4a — which colour (ADR-0139 / ADR-0144)

**Behaviour-changing → red.** The architect's declaration, per CLAUDE.md, with the reasoning stated because ambiguity resolves to red:

| Behaviour                                         | Test                                                                                                                                                                                                            | Expected red on the current tree                                                    |
| ------------------------------------------------- | --------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------- | ----------------------------------------------------------------------------------- |
| The console is given the hub URL (FR-002)         | New theory case `management-web` added to `Each_kiosk_instance_reaches_the_hub_on_layout_composition_directly`'s sibling for the console (in `AppHostOneOriginTests`, which already hosts console wiring tests) | `ShouldContainKey("VITE_LAYOUT_HUB_URL")` fails: key absent.                        |
| The hub admits the console (FR-003)               | The edited origins assertion above                                                                                                                                                                              | Expected `[api-gateway, kiosk-wall, kiosk-web, management-web]`, was three entries. |
| An unlisted origin is still refused (SC-004)      | **Already green and stays green**: `A_preflight_from_an_origin_that_is_not_configured_gets_no_allow_origin`. It is a guard, not new behaviour. The PR cites it rather than duplicating it.                      | n/a                                                                                 |
| The kiosks are unchanged (FR-006)                 | The existing kiosk theory and the kiosk/wall suites, **unmodified**                                                                                                                                             | n/a, characterisation: captured green before, green after.                          |
| The page's live update works (FR-001/002, SC-001) | One Playwright spec, `e2e/wall-detail-live-update.spec.ts`: open the wall detail page at the gateway origin, switch the wall's scene through the API, expect the page to re-read.                               | Fails today: the hub connection never establishes, so the page never re-reads.      |

A red observation on the **running stack** also comes first (FR-001): request the hub's negotiate endpoint from the console at `:5173` and
at `:5180` and record the failure, then fix, then record the same requests succeeding. That is the verification note's evidence (phase 5),
and it is the only thing that discharges premise 6.

A new test **class** needs an entry in the shard filter (`ci-shards/shard-N.filter`); tests are added to existing classes where they fit so
that none is needed. The one new e2e spec is registered with the e2e shard guard in the same commit.

## Project Structure

### Documentation (this feature)

```text
specs/495-console-hub-url/
├── spec.md
├── plan.md              # this file
├── research.md          # Phase 0
├── quickstart.md        # Phase 1
├── contracts/
│   └── console-hub-wiring.md
└── tasks.md             # /speckit-tasks, not this command
```

No `data-model.md`: the feature has no data, and the spec's two entities are existing concepts.

### Source Code (repository root)

```text
src/AppHost/WebApplications.cs                          # two additions, two corrected comments
docs/deployment-frontend-env.md                         # the admitted-origins sentence; the console's production requirement
tests/Integration.Tests/AppHostOneOriginTests.cs        # console reaches the hub directly
tests/Integration.Tests/AppHostKioskOneOriginTests.cs   # the edited origins assertion
tests/Integration.Tests/AppHostComposition/*.snapshot.txt  # three snapshots, regenerated after the red run
e2e/wall-detail-live-update.spec.ts                     # the end-to-end observation
```

**Structure Decision**: no new project, folder or abstraction. `apps/management-web` and `apps/shared` are read, not edited.

## Risks and what would change the plan

- **The wildcard in the realm's `redirectUris` does not matter here**, but cross-origin token use does: the hub request from `:5180` to
  `layout-composition` carries `Authorization`. The kiosks do the same from `:5180/kiosk/`, so the path is proven in principle, yet spec 494
  recorded that no sign-in at `:5180` had been _observed_ for any app before it. If the end-to-end run finds the console's hub request
  refused for a reason other than origin, the plan is wrong and goes back to the spec, not around it.
- **`:5173` is a fixed port**, so its origin is stable; if it is ever made non-fixed the endpoint reference already follows it.
- **The e2e spec needs a wall and a second scene** to switch between. If the seed has none, the spec creates them through the API and
  deletes them, as the other e2e specs do; if that proves heavy, the plan falls back to the stack observation plus the structural tests and
  says so in the verification note.
