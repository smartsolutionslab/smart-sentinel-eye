# Frontend deployment environment contract

Spec 011 (`specs/011-frontend-247-resilience/contracts/resilience-interfaces.md` §6).

Both SPAs (`management-web`, `kiosk-web`) are static Vite builds. Every
runtime origin they need is injected at build time via `VITE_*`
variables. **Production builds fail loudly at module load when a
required variable is missing** — a deliberate guard (FR-010) against
bundles that silently fall back to `localhost` or same-origin and
malfunction only at runtime on the fab floor.

| Variable | Consumed by | Dev default | Production |
|---|---|---|---|
| `VITE_API_GATEWAY_URL` | `apps/shared/src/api/gateway.ts` | same-origin (unit tests, previews) | **required** — public API-gateway origin |
| `VITE_KEYCLOAK_URL` | `apps/*/src/app/auth.ts` | `http://localhost:8080` | **required** — Keycloak origin; MUST match the issuer the services validate |
| `VITE_LAYOUT_HUB_URL` | `apps/shared/src/realtime/hubUrl.ts` | `/hubs/layouts` (Vite dev proxy) | **required** — absolute LayoutLifecycle hub URL; the dev-only Vite `/hubs` proxy does not exist in production |

"Production" means `import.meta.env.PROD === true`, i.e. any `vite build`.

## Dev (Aspire)

Aspire's AppHost injects `VITE_API_GATEWAY_URL` and `VITE_KEYCLOAK_URL`
into the Vite dev servers; the SignalR hub is reached through each
app's Vite `/hubs` proxy, so `VITE_LAYOUT_HUB_URL` stays unset. No
changes needed for local development.

## Prod (k3s / Helm)

The deploy layer must supply all three variables to the SPA build step
(or the Ingress must serve `/hubs` on the app origin, in which case
`VITE_LAYOUT_HUB_URL=/hubs/layouts` is still set explicitly). Verify a
candidate bundle by loading it once: a missing variable throws
`VITE_… must be set in production builds` in the console on first
paint.
