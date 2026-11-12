# Contract: the kiosk and wall paths and their `Ui` configuration

Extends `specs/491-gateway-single-origin-shell/contracts/gateway-paths.md`; replaces its row "`/kiosk/**`, `/wall/**` — reserved, not served
in this phase". Every other row and rule there is unchanged.

## Path map additions

| Path        | Served by               | Mode `Proxy` (dev)                                                 | Mode `Static` (built bundles)                                                                |
| ----------- | ----------------------- | ------------------------------------------------------------------ | -------------------------------------------------------------------------------------------- |
| `/kiosk`    | gateway                 | redirect to `/kiosk/`                                              | redirect to `/kiosk/`                                                                        |
| `/kiosk/**` | the kiosk (`kiosk-web`) | `kiosk-web` Vite dev server (5174), base `/kiosk/`, unchanged path | `Ui:Static:KioskRoot`; unclaimed path → the kiosk's `index.html` by the shared fallback rule |
| `/wall`     | gateway                 | redirect to `/wall/`                                               | redirect to `/wall/`                                                                         |
| `/wall/**`  | the wall (`kiosk-wall`) | `kiosk-wall` Vite dev server (5175), base `/wall/`                 | `Ui:Static:WallRoot`; unclaimed path → the wall's `index.html`                               |

## Rules

1. **Precedence.** `/kiosk/**` and `/wall/**` win over the shell's catch-all in both modes. A path under either prefix is never answered by
   the shell.
2. **Unconfigured instance.** With no `Ui:Proxy:Kiosk` (Proxy) or no `Ui:Static:KioskRoot` (Static), every `/kiosk/**` request is a gateway
   404; the same for the wall. The redirect for the bare prefix is still answered only when the instance is configured.
3. **Policies.** The four routes carry no `RateLimiterPolicy` and no `CorsPolicy`. Static files are served before the limiter.
4. **Fallback (Static).** The rule spec 493 sets for the shell, applied per prefix with that instance's `index.html`: a page navigation or an
   extension-less path gets `index.html`; a non-navigation request for a path with an extension that no file claims is a 404. Every response
   serving an instance's `index.html` carries `Cache-Control: no-cache` and `X-Content-Type-Options: nosniff`; every static file keeps
   `nosniff`.
5. **Fail fast.** A configured `KioskRoot`/`WallRoot` that is not a directory or has no `index.html` stops the gateway at start, naming the
   key.
6. **Not on the gateway:** `/hubs/**` (the hub is reached at the instance's own dev server, or the absolute production URL), WHEP (the
   server-provided `whepUrl`), Keycloak.

## `Ui` configuration additions

```json
"Ui": {
  "Proxy": { "Kiosk": "<kiosk-web dev server>", "Wall": "<kiosk-wall dev server>" },
  "Static": { "KioskRoot": "<kiosk build, base /kiosk/>", "WallRoot": "<wall build, base /wall/>" }
}
```

All four keys are optional and independent. The AppHost sets the two `Proxy` keys in run mode, beside spec 491's three.

## Kiosk environment

| Variable                 | `kiosk-web` (dev)                            | `kiosk-wall` (dev)                            | Production build                                                           |
| ------------------------ | -------------------------------------------- | --------------------------------------------- | -------------------------------------------------------------------------- |
| `VITE_KIOSK_MODE`        | unset                                        | `wall`                                        | unset for the kiosk build, `wall` for the wall build; **decides the base** |
| `VITE_API_GATEWAY_URL`   | the gateway's endpoint (unchanged)           | unchanged                                     | required, the gateway's public origin (unchanged, Q1 option A)             |
| `VITE_LAYOUT_HUB_URL`    | **new**: `<kiosk-web endpoint>/hubs/layouts` | **new**: `<kiosk-wall endpoint>/hubs/layouts` | required, absolute (unchanged)                                             |
| `VITE_LAYOUT_HUB_ORIGIN` | unchanged (builds the `/hubs` proxy)         | unchanged                                     | not used                                                                   |
| `VITE_KEYCLOAK_URL`      | unchanged                                    | unchanged                                     | required (unchanged)                                                       |

## Application contract

- Base: `import.meta.env.BASE_URL` (`/kiosk/` or `/wall/`); never re-derived in application code.
- Router basename: the base without its trailing slash.
- OIDC `redirect_uri`: `window.location.origin` + base + `oidc/callback`.
- Sign-in return: the stored `returnTo` only when it is a path that starts with the base (`/kiosk/...`); otherwise the base. Never `/`,
  never `//...`, never another origin.
