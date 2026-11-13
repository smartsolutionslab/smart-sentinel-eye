# Feature Specification: The operator console's live updates reach the hub

**Feature Branch**: `495-console-hub-url`

**Created**: 2026-11-13

**Status**: Draft, clarified (Q1 answered by the owner, 2026-11-13: option C); no markers remain

**Issue**: #3263, a follow-up of epic #3248. **Governing decisions**: ADR-0192 decision 3 (the hub stays direct) and ADR-0152 (SignalR is
the real-time transport); both **unchanged** by this spec. **Builds on**: specs 491 (#3253) and 494 (#3266). **Lane**: supervised (ADR-0037).

**Input**: User description: "Route the SignalR layout hub through the API gateway so the operator console's wall detail page's live
updates work at the one origin." **Q1 (owner, 2026-11-13):** "My goal is a single endpoint for usage, one URL, and for performance and
technical reasons other internal endpoints are allowed." That reads the goal as one URL _for a person_, and the hub as an internal endpoint
the page connects to, so the hub stays direct and the console is given its address the way the kiosks already are.

**The principle this spec follows.** A person opens **one URL**. Internal endpoints the page connects to behind the scenes (the layout hub,
media, Keycloak) are allowed to be separate where there is a performance or technical reason. For the hub the reason is §IV: the
event-to-overlay path is a per-kiosk real-time push, and ADR-0192 decision 3 keeps it off the gateway on purpose.

**Latency budget (§IV): legs touched: none.** The wall detail page is an operator console page, not the screen the budget ends on. It only
_re-reads_ a wall's state when the hub says the scene changed.

| Leg                   | Why it does not move                                                                                                                                                                                  |
| --------------------- | ----------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------- |
| Camera → SFU          | Not a browser leg.                                                                                                                                                                                    |
| SFU → kiosk decode    | Media stays direct and is not touched.                                                                                                                                                                |
| Presentation buffer   | In the kiosk; not touched.                                                                                                                                                                            |
| Event → overlay state | The kiosk and the wall display keep their direct hub connection, unchanged (FR-006). The console's connection is the same kind of direct connection, to the same service; it adds no hop to anyone's. |
| Composite + render    | In the kiosk; not touched.                                                                                                                                                                            |
| Headroom              | Untouched.                                                                                                                                                                                            |

## Premises checked against the tree (2026-11-13, `origin/develop` at `56dd435b`)

Issue #3263 says it was "read from the code, not observed on a running stack". It was read again before anything was specified on it.

| #   | Claim in #3263                                                                       | Verdict                        | Evidence                                                                                                                                                                                                                                                                                          |
| --- | ------------------------------------------------------------------------------------ | ------------------------------ | ------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------- |
| 1   | The wall detail page opens the hub at a page-relative path                           | **True**                       | `WallDetailPage.tsx` calls `createLayoutHubClient`; `apps/shared/src/realtime/hubUrl.ts` returns `/hubs/layouts` when `VITE_LAYOUT_HUB_URL` is unset and the build is not production, and **throws at module load in a production build** when it is unset.                                       |
| 2   | `management-web` has no `/hubs` proxy                                                | **True**                       | `apps/management-web/src/app/devProxy.ts` proxies `/api`, `/remotes/<name>` and the bare REST contexts; no hub entry. The AppHost sets `VITE_LAYOUT_HUB_URL` only on `kiosk-web` and `kiosk-wall` (`WebApplications.cs:174`, `:205`).                                                             |
| 3   | The gateway does not route `/hubs`                                                   | **True, and by decision**      | ADR-0192 decision 3 keeps it off the gateway, and the boundary test enforces it. A `/hubs/layouts` request at the gateway origin falls to the catch-all and reaches the shell's dev server, which does not serve it.                                                                              |
| 4   | The fix needs "the hub CORS policy on layout-composition" first                      | **Now satisfied**              | Spec 494 merged it (`LayoutHubCors.cs`, `AddLayoutHubCors` / `UseLayoutHubCors` in `Program.cs`). The AppHost lists three origins: the two kiosks and the gateway. **The shell's own port, `:5173`, is not among them.**                                                                          |
| 5   | The suggested fix: set `VITE_LAYOUT_HUB_URL` on `management-web` and admit the shell | **True, and it is the design** | The kiosks already work this way (`LayoutHubUrl(...)`, `WebApplications.cs:156`). `docs/deployment-frontend-env.md` already makes `VITE_LAYOUT_HUB_URL` **required** for a production build and says the hub is cross-origin from the page, so `layout-composition` must admit the page's origin. |
| 6   | The breakage is observable                                                           | **Not yet observed**           | No run has requested the hub from `:5173` or `:5180`. FR-001 makes the broken state an observation first (ADR-0139).                                                                                                                                                                              |

Found while checking, not named by the issue:

- **The console would break in production regardless.** `hubUrl.ts` throws at module load when `VITE_LAYOUT_HUB_URL` is unset in a
  production build, and the AppHost's production publish never sets it for the console. This feature sets it for the console in dev; the
  production wiring belongs with the edge (#1015) and is out of scope, but FR-007 records the requirement so it is not discovered later.
- **The AppHost comment contradicts the fix.** `WebApplications.cs:21` states "management-web's vite.config.ts reads no environment and has
  no such proxy". Once the console is given the hub URL, that line describes a state that no longer exists (FR-008).

## User Scenarios & Testing _(mandatory)_

### User Story 1 - Live scene changes reach the operator's wall page (Priority: P1)

An operator opens a wall's detail page in the management console, at the one origin and at the console's own dev port. Another operator, or
a rule, switches the wall's scene. The page updates without a reload.

**Why this priority**: It is the whole defect. Today the page's live connection never establishes, so the operator sees a stale scene and
nothing says so.

**Independent Test**: With the stack running, open the wall detail page at the gateway origin, switch the wall's scene from elsewhere, and
observe the page re-read the wall. Repeat at the console's own port.

**Acceptance Scenarios**:

1. **Given** a signed-in operator on a wall's detail page at the gateway origin, **When** the wall's scene is switched elsewhere, **Then**
   the page shows the new scene without a reload.
2. **Given** the same page opened at the console's own dev port, **When** the scene is switched, **Then** the same happens.
3. **Given** the connection drops and recovers, **When** it reconnects, **Then** the page re-reads the wall (existing behaviour, unchanged).

---

### User Story 2 - The person still uses one URL (Priority: P1)

The operator types the gateway's address and nothing else. They are never asked for, shown, or required to configure a hub address.

**Why this priority**: It is the owner's goal. The hub being a separate internal endpoint is acceptable only if it stays invisible to the
person using the console.

**Independent Test**: Open the console at the gateway origin only; the wall page's live updates work with no second address typed, bookmarked
or entered.

**Acceptance Scenarios**:

1. **Given** only the gateway's address, **When** an operator uses every console page including the wall detail page, **Then** no page asks
   for another address.

---

### User Story 3 - The kiosk and the wall display are unaffected (Priority: P1)

A kiosk or a wall display keeps reaching the hub the way it does today. Nothing on the event-to-overlay path changes.

**Why this priority**: §IV.

**Independent Test**: The existing kiosk and wall suites pass unmodified; the kiosks' hub URL is unchanged.

**Acceptance Scenarios**:

1. **Given** a kiosk at `/kiosk/` and a wall at `/wall/`, **When** this feature is in place, **Then** each connects to the hub exactly as it
   did before it.

---

### Edge Cases

- The console opened at its own dev port and at the gateway origin are **two different origins** to the browser; the hub must admit both,
  and a third origin that is not listed must still be refused (the policy is an exact-origin list, not a wildcard).
- A stack started outside Aspire (`npm run dev` alone): the console's hub URL is unset. It keeps today's behaviour, a page-relative path
  that does not reach the hub, and must not crash on load in dev.
- The hub's address changes between runs (Aspire assigns the port): the console must read it from the AppHost, not from a fixed number.
- The access token travels with the hub request. Cross-origin, it is sent exactly as the kiosks send it today; nothing new is exposed.
- The hub is unreachable when the page loads: today nothing says so (see FR-009).

## Requirements _(mandatory)_

### Functional Requirements

- **FR-001**: The defect MUST be observed before it is fixed: a request for the layout hub made from the console's origin, at the one origin
  and at the console's own port, MUST be recorded failing on the pre-change tree (ADR-0139). The failure is quoted in the PR.
- **FR-002**: In the dev stack the console MUST be given the layout hub's address by the AppHost, the same way the kiosks are, and the wall
  detail page MUST connect to it.
- **FR-003**: `layout-composition` MUST admit the console's origins: the gateway origin (already admitted) and the console's own dev port.
  An origin that is not listed MUST still be refused; no wildcard is introduced.
- **FR-004**: The hub MUST stay off the gateway. `ApiGateway_does_not_sit_on_the_realtime_or_media_latency_legs` MUST stay in force and
  unmodified, and ADR-0192 decision 3 MUST stay true. This feature changes no ADR.
- **FR-005**: A person MUST NOT need to know, type or configure the hub's address to use the console (User Story 2).
- **FR-006**: The kiosks and the wall display MUST keep their direct connection and their configuration, unchanged, and the §IV
  event-to-overlay leg MUST gain no hop.
- **FR-007**: The console's production build MUST NOT be left without a hub address by accident: the requirement that
  `VITE_LAYOUT_HUB_URL` be set for the console in production is recorded (it is already in `docs/deployment-frontend-env.md`), and the
  production wiring is explicitly deferred to the edge work (#1015), not silently assumed.
- **FR-008**: The AppHost comment that states the console has no hub address (`WebApplications.cs:21`) MUST be corrected to the new truth,
  in the lines this change already touches.
- **FR-009**: When the console's hub connection cannot be established, the operator SHOULD be told, and the page MUST NOT present stale data
  as live. _(Today the failure is silent. This is new behaviour beyond #3263's wording; the plan states whether it ships in this slice or
  becomes its own issue.)_

### Key Entities _(include if feature involves data)_

- **Layout hub connection**: a long-lived, authenticated real-time connection from a page to `layout-composition`, used to be told that a
  wall's scene changed. It carries a bearer token and is cross-origin from the page.
- **One origin**: the gateway's address, from which the console, the kiosks and the wall display are served (ADR-0192). It is the address a
  _person_ uses; it is not claimed to be the only address a page ever contacts.

## Success Criteria _(mandatory)_

### Measurable Outcomes

- **SC-001**: On a running stack, an operator who switches a wall's scene sees the wall detail page update within 2 seconds, without
  reloading, at both the one origin and the console's own port.
- **SC-002**: A person using the console at the gateway origin enters or sees **one** address and no hub address.
- **SC-003**: The kiosk, wall-display, gateway and architecture test suites that passed before this change pass **unmodified**, and the
  event-to-overlay path has the same number of network hops as before.
- **SC-004**: A browser at an origin that is not in the hub's allowed list is still refused (a counterfactual test, not an assumption).

## Assumptions

- **The hub stays direct.** This is the owner's answer to Q1 and ADR-0192 decision 3's standing position. The gateway route, the dev-proxy
  entry and any change to the gateway's boundary test were considered and **not** chosen.
- **Kiosks are unchanged.** They are outside the request and already work this way.
- **Authentication is unchanged.** The hub validates the bearer token itself.
- **Out of scope:** Keycloak behind the gateway, WebRTC/WHEP, any change to `@microsoft/signalr`, the per-instance hub backplane
  (ADR-0153), the production edge (#1015), and the other open follow-ups of #3248 (#3247, #3252).
- **No ADR is written.** Nothing here needs one: it implements decision 3 rather than amending it.
