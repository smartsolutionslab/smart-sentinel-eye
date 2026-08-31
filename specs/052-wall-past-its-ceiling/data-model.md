# Data model — 052 a wall past its ceiling

Phase 1. No database, no schema, no migration. These are configuration shapes
and one sequence of administrative calls.

---

## 1. Screen mode

The single deployment input, chosen so there is **no half-configuration**. One
value decides both the client and what it asks for; nothing can select one
without the other.

| Mode | Client | Requests | Carries | Drops out at the ceiling |
|---|---|---|---|---|
| `default` (absent) | `kiosk-web` | `openid` | today's scopes, incl. `sse.events.write` | yes, as now |
| `wall` | `kiosk-wall` | `openid offline_access` | five **read** scopes, `sse-identity`, `sse-groups` | **no** |

**Misconfiguration is bounded and asymmetric**, which is why it is modelled:

| | What happens |
|---|---|
| wall mode, operator signs in | refused — the account lacks the privilege (`not_allowed`) |
| default mode, wall account signs in | signs in normally; simply no long grant, drops out as today |

The second is benign. The first must **not** be treated as recoverable — see §4.

---

## 2. The wall client

`kiosk-wall`: public, standard flow, the same redirect URIs as `kiosk-web`.

| | |
|---|---|
| default scopes | `sse-identity`, `sse-groups`, `sse.cameras.read`, `sse.streams.read`, `sse.layouts.read`, `sse.overlays.read`, `sse.variables.read` |
| optional scopes | `offline_access` |
| **absent** | **`sse.events.write`** |

**The absence is the design.** Scopes belong to clients, so a separate client is
the only place a wall display's authority can be narrowed. Spec 050 wrote that
such an account could change nothing while its grant carried a write scope; here
the authority is not in the grant to begin with.

`offline_access` is **optional, never default**. A default scope is mandatory:
the provider refuses the whole sign-in for any account without the matching
privilege, which is what made the previous attempt unshippable.

---

## 3. The containment

Two calls, in this order. **The shape is load-bearing** — a role object obtained
any other way returns 404, which reads like a permission failure and is not.

1. `GET /users/{id}/role-mappings/realm` → the **direct** mappings. There is
   exactly one: `default-roles-<realm>`.
2. `DELETE /users/{id}/role-mappings/realm` with that same list.

| Property | Measured |
|---|---|
| Authority needed | **none new** — what the identity service already holds |
| Effective roles afterwards | **none** |
| Kiosk still obtains a token | **yes** |
| Run twice | **idempotent** |
| Anything authorising on the removed roles | **no** — access is by scope and fab group |

### When it runs

| Occasion | Why |
|---|---|
| **During enrolment**, right after the client exists | the account cannot exist earlier; the window is one call wide |
| **At startup**, over accounts enrolment created | covers kiosks enrolled before this feature, and catches drift |

**If it fails during enrolment, the enrolment fails.** An enrolment reporting
success while leaving a privilege holder behind is the outcome worth avoiding,
and idempotency makes the retry safe.

**It is reachable only for accounts enrolment created.** It removes *every*
direct realm mapping; against a human account that would be destructive.

---

## 4. What a failure means to a screen (amending spec 051)

Spec 051 classifies by reported code and treats unrecognised codes as
**recoverable**, deliberately — a wrong "terminal" darkens a wall.

`not_allowed` is added to the **refused** set.

**Why it must change here**: it is what a wall-mode screen receives when an
operator signs into it, and this feature is what makes that code reachable.
Without the change such a screen retries forever behind "Reconnecting",
telling whoever reads it that the problem will clear. It will not — the account
simply lacks the privilege, and no number of attempts alters that.

---

## 5. What a wall display's grant is worth

| | Value |
|---|---|
| Lifetime | no expiry while used |
| What ends it unused | the provider removes an unused offline session after **30 days** — a provider default this repository does not set |
| Reach | one fab, by group membership |
| Authority | **read only**, by the client's scopes |
| Revocation | per session; ending one screen's leaves others running |
| Audit identity | the wall-display account, **not the screen** — per-device identity is filed and unbuilt |

**Both directions, because the previous attempt got both wrong**: a stolen
powered-off screen is the *unused* case and expires in thirty days — smaller than
"never expires" suggests. And a screen legitimately switched off for longer than
thirty days needs a person — so the availability guarantee is weaker than "it
never drops out" suggests.

---

## 6. What is deliberately not modelled

- **Accounts created by hand** in the provider's console — FR-002a, filed.
- **The provider's session-timing defaults** — unset by this repository; pinning
  them is a separate decision.
- **Per-device identity** — a subsystem, filed.
- **Rotation of a wall-display credential** — nothing rotates it, and the record
  says so rather than implying otherwise.
