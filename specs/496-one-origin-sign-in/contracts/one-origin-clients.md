# Contract: what the three browser clients declare

There is no new HTTP surface. The contract is the realm declaration of three clients and the rule that joins it to the apps.

| Client           | App              | Served from (dev)                                  | Returns after sign-in to                                          | `redirectUris` after this feature                                              | `webOrigins` |
| ---------------- | ---------------- | -------------------------------------------------- | ----------------------------------------------------------------- | ------------------------------------------------------------------------------ | ------------ |
| `management-web` | operator console | `http://localhost:5173/`, the one origin `/`       | `${origin}/`                                                      | `http://localhost:5173/*`, **`http://localhost:5180/*`**, `http://localhost:*` | `["+"]`      |
| `kiosk-web`      | kiosk            | `http://localhost:5174/`, the one origin `/kiosk/` | `${origin}/kiosk/oidc/callback` (`${origin}${base}oidc/callback`) | `http://localhost:5174/*`, **`http://localhost:5180/*`**, `http://localhost:*` | `["+"]`      |
| `kiosk-wall`     | wall display     | `http://localhost:5175/`, the one origin `/wall/`  | `${origin}/wall/oidc/callback`                                    | `http://localhost:5175/*`, **`http://localhost:5180/*`**, `http://localhost:*` | `["+"]`      |

**The joining rule**: with `webOrigins: ["+"]`, the allowed web origins are the origins of the _explicit_ `redirectUris`. Every origin an app is
served from MUST therefore appear as an explicit entry; a wildcard contributes no origin. After this feature those are each client's own port and
the one origin.

**What does not change**: `webOrigins`, the wildcard entry, every scope, audience, grant and attribute of the three clients, every other client
(including the persona client, which must keep **no** redirect URIs), and the apps' own redirect construction.

**The guard**: `WallClientDeclarationTests.A_client_names_the_origin_it_is_served_from` asserts, per client, that an explicit entry contains
`localhost:<port>` for the client's own port and for `5180`. The browser spec `e2e/one-origin-sign-in.spec.ts` is what shows the declaration
_holds_.
