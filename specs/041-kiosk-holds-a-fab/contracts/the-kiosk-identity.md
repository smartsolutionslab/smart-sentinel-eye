# Contract: the kiosk identity

**Feature**: 041 | **Date**: 2026-08-25

There is no HTTP contract in this feature — no endpoint is added, removed or
reshaped. What *is* a contract is the identity a kiosk presents and what the
system will accept from it. It is written down here because it is currently
agreed by coincidence in four places and enforced in none.

---

## 1. What a kiosk holds

**One definition, three holders.** `KeycloakScopeBundles.Kiosk` is the single
notion of what a kiosk may do. It is granted to:

| Holder | How | Today |
|---|---|---|
| An enrolled physical kiosk device | `EnrollKioskCommandHandler` -> `KeycloakScopeBundles.Kiosk` | correct |
| The browser kiosk (`kiosk-web` realm client) | realm JSON `defaultClientScopes` | correct, unused |
| The browser kiosk app | `client_id` in `auth.ts` | **wrong — points at the legacy client** |

```text
sse.cameras.read
sse.streams.read
sse.layouts.read
sse.overlays.read
sse.variables.read
sse.events.write
```

Plus `sse-groups`, which is not a permission — it is what puts `/fabs/<id>` into
the token. A holder without it can do nothing anywhere, which is the defect.

**Compared as a set, both directions.** A scope added to the realm client and not
to the bundle is drift; so is the reverse. Neither is a spot check.

`sse.events.write` stays in the set even though the browser kiosk never writes.
FR-004 says there is one notion of what a kiosk may do, and a browser-only
variant would be a second one.

---

## 2. What a kiosk does not hold

`sse.management` — the grandfathered legacy bundle that satisfies every `sse.*`
policy except `sse.events.publish`.

**Asserted as an absence.** A check that only confirms the kiosk works passes
just as happily with `sse.management` restored, which is how the weakness comes
back. SC-002 is the assertion that cannot be satisfied by a working kiosk.

---

## 3. What the token must carry

| Claim | Source | Needed by |
|---|---|---|
| `groups: ["/fabs/<id>", ...]` | `sse-groups` scope's group-membership mapper | every fab-scoped read; `LayoutLifecycleHub`'s group join |
| `scope` | the six default client scopes | every endpoint policy |
| `sub` | **a client-level `oidc-sub-mapper` added by this feature** | `WhepAuthValidator` — no `sub`, no video |

`sub` is on this list because the realm has no `basic` scope (research R2) and
`kiosk-web` holds no scope that emits one. It is an identity claim, not a
permission: adding it changes nothing about what the kiosk may do.

`preferred_username` is **not** on this list. Nothing in the kiosk or in any
endpoint it calls reads it. It is not added.

---

## 4. What the system asks of a kiosk

Every gate on a kiosk's path must be satisfiable by the set in §1.

| Gate | Requires | Before | After |
|---|---|---|---|
| Layout, overlay, variable, stream reads | the matching granular scope, or `sse.management` grandfathered | ok | ok |
| `/hubs/layouts` | `sse.layouts.read`, ditto | ok | ok |
| `POST /streams/kiosk-latency` | `sse.streams.read`, ditto | ok | ok |
| **`POST /streams/authorize`** (WHEP) | **`sse.management` only, by hand** | **refuses every kiosk** | `sse.streams.read`, or `sse.management` grandfathered |

The last row is the contract this feature repairs. It is the only gate in the
product that does not use `RequireScopeExtensions`' rule, and the only one a
view-only persona cannot pass.

**This is a change to the gate, not to the set.** The kiosk's permissions are
identical before and after; a call it was entitled to make stops being refused.

---

## 5. What must fail

A check that cannot fail is not a check. These are the failures this feature's
assertions must actually produce:

| Break this | Expected |
|---|---|
| Point `auth.ts` back at the legacy client | the kiosk e2e goes **red** (SC-004) — demonstrated by causing it, not by reasoning |
| Add a scope to the realm's `kiosk-web` | `KioskScopeParityTests` fails |
| Add a scope to `KeycloakScopeBundles.Kiosk` | the same test fails |
| Restore `sse.management` on the kiosk | the token assertion fails, though every behavioural check still passes |
| Remove the `sub` mapper | video stops; **no automated check catches it** — Phase 5 only |

The last row is stated rather than solved. CI cannot produce video
(`camera-sim` and `scenario-simulator` are excluded from e2e mode), so the WHEP
path has no automated coverage in either direction. Claiming otherwise would be
the same error this feature and spec 040 both exist to correct.
