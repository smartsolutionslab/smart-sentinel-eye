# Research: The operator console's live updates reach the hub

Every unknown in the plan's Technical Context was resolved by reading the tree; none needed an outside source.

## R1 — Can the console be given the hub URL the way the kiosks are?

**Decision**: yes; reuse `LayoutHubUrl(layoutComposition)`.
**Rationale**: `AddManagementWeb` already receives `layoutComposition` (the parameter exists in its signature), and `LayoutHubUrl` is a private
static helper in the same file that returns `ReferenceExpression.Create($"{endpoint}/hubs/layouts")`. The kiosks' test pins its shape
(`Format == "{0}" + LayoutLifecycleHub.Path`), so the console gets the identical expression.
**Alternatives considered**: a `/hubs` proxy in `devProxy.ts` plus a gateway route (rejected by the owner, Q1; conflicts with ADR-0192
decision 3 and the boundary test); a fixed `http://localhost:<port>` literal (rejected: Aspire assigns the port per run).

## R2 — Does `layout-composition` already admit the right origins?

**Decision**: it admits the gateway (`:5180`) already; add `management-web` (`:5173`).
**Rationale**: `WebApplications.cs` lists `kiosk-web`, `kiosk-wall` and `api-gateway`. The policy refuses wildcards and non-origin strings at
start (`LayoutHubCors.AddLayoutHubCors`) and answers only the hub path (`UseLayoutHubCors`). Adding an `EndpointReference` keeps both
properties.
**Alternatives considered**: a configured string (rejected: the existing entries are endpoint references so the port follows Aspire).

## R3 — Does the page need any code change?

**Decision**: no.
**Rationale**: `WallDetailPage.tsx` passes an access-token factory and callbacks to `createLayoutHubClient`; the URL comes from
`layoutHubUrl` in `apps/shared/src/realtime/hubUrl.ts`, which returns `VITE_LAYOUT_HUB_URL` when set. Cross-origin use is already how the
kiosks run (`credentials` and the `Authorization` header are in the policy's allowed set).

## R4 — Is the failing behaviour real, and does anything already exercise the console's hub path?

**Decision**: unknown until observed; it is the first task.
**Rationale**: grep finds no test that asserts the console's hub address or exercises `WallDetailPage` against a live hub; `WallDetailPage.test.tsx`
renders without an auth provider and does not reach SignalR. No e2e spec opens the wall detail page.

## R5 — The environment contract for production

**Decision**: record, do not wire.
**Rationale**: `docs/deployment-frontend-env.md` already marks `VITE_LAYOUT_HUB_URL` required for a production build, and `hubUrl.ts` throws
at module load without it. Nothing yet builds the console for production with that value; that belongs with the edge (#1015) and FR-007
asks only that the requirement is written down for the console as it already is for the kiosks.
