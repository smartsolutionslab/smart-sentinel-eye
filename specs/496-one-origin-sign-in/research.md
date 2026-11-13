# Research: Sign-in at the one origin

Every unknown in the plan's Technical Context was resolved by reading the tree or the running stack; none needed an outside source.

## R1 — Why does sign-in fail at `:5180` and not at `:5173`?

**Decision**: the browser's token request from `http://localhost:5180` is not admitted by Keycloak, because no client names that origin.
**Rationale**: observed (spec 495 verification): `POST …/protocol/openid-connect/token` from `http://localhost:5180` blocked by CORS. The realm
declares `webOrigins: ["+"]` for all three browser clients, and `"+"` means "the origins of the explicit redirect URIs"; the only entries that
name an origin are the per-port ones, and `http://localhost:*` is a wildcard that yields none. `WallClientDeclarationTests` documents this
mechanism and records that the wall client failed this way once.
**Alternatives considered**: a Keycloak-side CORS misconfiguration, or the gateway stripping a header (rejected: the failing request goes to
Keycloak directly, not through the gateway; `:5173` against the same Keycloak works).

## R2 — Which fix?

**Decision**: add `http://localhost:5180/*` to each client's `redirectUris`; keep `webOrigins: ["+"]`.
**Rationale**: smallest change, the repo's documented mechanism, keeps the list exact. The redirect each app uses after sign-in
(`${origin}${base}oidc/callback` for the kiosks; `${origin}/` for the console) falls under `http://localhost:5180/*`.
**Alternatives considered**: an explicit `webOrigins` list (a larger change, three more strings to keep in step, and the existing guard
reasons in terms of redirect URIs); removing the `http://localhost:*` wildcard (a separate security decision that could break other local ports,
recorded in the spec's Assumptions); one path-specific entry per app (exact, but three different strings for one fact).

## R3 — Is the realm declared anywhere else?

**Decision**: no; one file.
**Rationale**: `src/AppHost/Realms/smart-sentinel-eye-realm.json` is imported by the AppHost (`WithRealmImport("../AppHost/Realms")`) and read by
the architecture tests; `master-realm.json` has no browser client. A grep for `redirectUris` across `src`, `tests`, `e2e`, `scripts`, `docs` and
`.github` finds the realm file and one test (`PasswordGrantClientTests`) that asserts the _persona_ client has **no** redirect URIs, which this
change does not touch. The file starts with a UTF-8 byte-order mark; an edit must preserve it.

## R4 — What reaches a running Keycloak?

**Decision**: nothing but a fresh import.
**Rationale**: ADR-0131: "A realm JSON edit does not reach a running identity provider. Its volume persists, so the file describes the next fresh
import and nothing else. Anyone verifying a realm change on a live stack must apply it through the admin API and say which they tested." CI and
the e2e job boot with `PersistentStack=false`, so they import fresh. A developer's persistent stack must drop `keycloak-data` or use the admin
API; the plan's documentation bullet says so.

## R5 — Can the browser check run in CI at `:5180`?

**Decision**: yes.
**Rationale**: the e2e job runs the AppHost in run mode, which the `EndToEndJob` composition snapshot shows with the gateway on `Port=5180` and
`Ui__Mode="Proxy"`. The Playwright suite has no managed server, so a spec may use absolute `http://localhost:5180/…` URLs. The e2e shard
coverage guard derives the set from `--list`, so a new spec needs no registration.

## R6 — What does each app show on arrival, and is seed data needed?

**Decision**: console: heading _Cameras_; kiosk and wall display: heading _Pick a layout_; no seed data needed for the heading.
**Rationale**: `signInAsOperator` asserts _Cameras_; `signInToKiosk` and `signInAsWallDisplay` assert _Pick a layout_ (the kiosk helper also
requires a populated picker, which the `seed` project provides; the spec does not need that). Credentials come from those helpers:
`operator` / `Cobalt-Meadow-Ripple-24` for the console and the kiosk, `wall-munich` / `Copper-Lantern-Drift-47` for the wall display.

## R7 — How is an unlisted origin shown to be still blocked, without a browser?

**Decision**: a token request with `Origin: <unlisted>` and a bogus code.
**Rationale**: Keycloak adds `Access-Control-Allow-Origin` to a token response only when the origin is allowed for the requesting client. The
generic preflight is not a usable probe: it answered every origin, including an unlisted one, in spec 495's check. The `POST` carries the
`client_id`, so the allow decision is the client's.
