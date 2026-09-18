# Spec 180 — A disable that checks the fab

**Issue:** [#2240](https://github.com/smartsolutionslab/smart-sentinel-eye/issues/2240)
— on Project #13, status **Todo**, label `agent:ready`. Verified by `content.url`
against a `--limit 2000` dump, not by the number filter. **No `item-add` needed.**

**ADRs referenced:** ADR-0114 (fab resolution and the guard's scope), ADR-0041
(repository contract), ADR-0047 / ADR-0089 (`Result<T, Error>` + `ApiError`),
ADR-0105 (`Ensure.That`), ADR-0139 + constitution §Testing (observed red),
ADR-0103 (integration via the Aspire fixture), ADR-0036 (smallest change),
ADR-0037 / ADR-0144 (the lane).

**Constitution:** §VIII — *"Authorization is enforced by scope checks at every
endpoint, plus fab-group membership."* Two routes are the counterexample. This
spec does not amend that sentence; it makes it true of them.

**Latency budget (§IV): N/A.** Identity is an administrative write path. Nothing
here touches any of the six legs of `event arrival → overlay rendered`.

---

## 1. The defect, confirmed by content rather than by line number

The issue cites `KiosksEndpoints.cs:50` and `DevicesEndpoints.cs:50`. Those line
numbers have drifted; the code has not. Confirmed 2026-09-18 on `develop`:

- `src/Identity/Api/KiosksEndpoints.cs:198-221` — `Disable` takes
  `(string clientId, DisableKioskCommandHandler handler, CancellationToken)`.
  It does **not** take `ClaimsPrincipal` or `IFabAuthorizationGuard` at all, so
  no fab check is possible in it, let alone absent by oversight.
- `src/Identity/Api/DevicesEndpoints.cs:202-225` — identical shape.
- `src/Identity/Application/Commands/Handlers/DisableKioskCommandHandler.cs:22-27`
  — `clients.GetByClientIdAsync(command.ClientId, …)` then
  `found.Value.Kind != ClientKind.Kiosk`. **The kind is the only check.**
- `DisableDeviceCommandHandler.cs:22-27` — same, with `ClientKind.Device`.
- `DisableKioskCommand` / `DisableDeviceCommand` carry **one** member, `ClientId`.
  There is nowhere for a fab to travel even if the endpoint resolved one.

Both endpoint files record the absence as deliberate, in a comment above the
route table (`KiosksEndpoints.cs:33-38`, `DevicesEndpoints.cs:33-38`):

> *"On `DELETE /kiosks/{clientId}` that is the only producer, because it runs no
> fab guard; the other two can reach 403 through `IFabAuthorizationGuard` as
> well."*

That comment is accurate today and must be corrected by this change, not left
behind — a stale comment asserting the absence of a control that now exists is
the same clerical defect §IV's leg table has twice had to be corrected for.

**Reachability.** `RequireScopeExtensions.LegacyManagementBundle` makes
`sse.management` satisfy every `sse.*` policy except `sse.events.publish`, and
`smart-sentinel-eye-web` carries `sse.management` as a default client scope. So
**every seeded operator in the realm can already call both routes** — the attack
does not need a specially-scoped principal.

**Why now.** Spec 121 (#2165 / #2207) fixed `DisableClientAsync` serialising
`{ enabled = false }` to `{}` under `JsonIgnoreCondition.WhenWritingDefault`.
Before it, the cross-fab request marked a row and left the Keycloak client
enabled — the wall kept running. After it the call does what it always claimed.
That fix is correct and is not touched here.

## 2. Scope — and what is deliberately not in it

The issue's own body asks for a sweep of other Identity write paths. **That sweep
is done** and its answer is in the issue's comment (2026-09-13): **#2280**
(webhook rotation resolves by name and guards the fab the caller *typed*) and
**#2281** (the three Identity list endpoints return every fab's clients when
`fabId` is omitted). Both are filed separately and are **out of scope here** — no
re-audit, no folding in. `WebhookRotationEndpoints.cs` and the three `List`
handlers are on the forbidden list in `tasks.md`.

**Both disable routes are one slice, not two.** Same file shape, same handler
shape, same command shape, same repository call, one shared integration fixture.
Splitting them would ship a release in which half the defect is live, and the
second half would re-touch every file the first touched.

## 3. User stories

### US1 (P1) — A disable is confined to a fab the caller holds

*As an operator holding `sse.identity.kiosks.write` (or `.devices.write`) in one
fab, I can disable kiosks and devices in my own fab and cannot reach another
fab's, whether I name their fab or mine.*

Independently shippable and independently observable: one HTTP call against the
running stack shows the refusal, and one shows the legitimate disable still
working. Nothing else in the repo depends on it landing first.

**This is the whole of P1. There is no P2.**

## 4. Acceptance scenarios (Gherkin)

Every scenario below is written for `/kiosks` and holds identically for
`/devices` with `DEVICE_` in place of `KIOSK_`.

### AS-1 — happy path (own fab)

```gherkin
Given a kiosk "wall-a" enrolled in fab "munich"
  And an operator whose groups claim contains "/fabs/munich"
When they send DELETE /kiosks/wall-a?fabId=munich
Then the response is 200 OK carrying the RegisteredClientIdentifier
  And the Keycloak client "wall-a" is disabled
  And the registered_clients row records DisabledAt
```

### AS-2 — the cross-fab attack, naming the victim's fab

```gherkin
Given a kiosk "wall-a" enrolled in fab "munich"
  And an operator whose groups claim contains only "/fabs/dresden"
When they send DELETE /kiosks/wall-a?fabId=munich
Then the response is 403 with title "RESOURCE_FAB_NOT_AUTHORIZED"
  And the Keycloak client "wall-a" is still enabled
  And the registered_clients row still has DisabledAt null
```

### AS-3 — the cross-fab attack, naming the attacker's own fab

```gherkin
Given a kiosk "wall-a" enrolled in fab "munich"
  And an operator whose groups claim contains only "/fabs/dresden"
When they send DELETE /kiosks/wall-a?fabId=dresden
Then the response is 404 with title "KIOSK_NOT_FOUND"
  And the Keycloak client "wall-a" is still enabled
```

**AS-3 is the scenario a guard alone does not cover, and it is why the fix has
two halves.** `EnsureAccessAsync(user, "dresden")` succeeds — the caller
genuinely holds dresden. Only a lookup *scoped to* the named fab refuses. A fix
that adds the guard and leaves `GetByClientIdAsync` unscoped closes AS-2 and
leaves AS-2's whole exploit available under AS-3's spelling.

404 rather than 403, matching `CameraEndpoints`' declared choice
(`src/CameraCatalog/Api/CameraEndpoints.cs:104-106`): another fab's resource is
indistinguishable from one that does not exist, so the route cannot be used to
enumerate what another plant runs.

### AS-4 — bad request

```gherkin
Given an operator in fab "munich"
When they send DELETE /kiosks/wall-a with no fabId
Then the response is 400 with title "KIOSK_INVALID_INPUT"
  And nothing is disabled
```

```gherkin
Given an operator in fab "munich"
When they send DELETE /kiosks/wall-a?fabId=%20
Then the response is 400 with title "KIOSK_INVALID_INPUT"
```

### AS-5 — auth

```gherkin
Given a caller with no bearer token
When they send DELETE /kiosks/wall-a?fabId=munich
Then the response is 401
```

```gherkin
Given a caller authenticated without sse.identity.kiosks.write
  And without the legacy sse.management bundle
When they send DELETE /kiosks/wall-a?fabId=munich
Then the response is 403
  And the refusal comes from the scope policy, before any fab is read
```

### AS-6 — unchanged behaviour that must stay unchanged

```gherkin
Given no client with clientId "ghost" exists
When a munich operator sends DELETE /kiosks/ghost?fabId=munich
Then the response is 404 with title "KIOSK_NOT_FOUND"
```

```gherkin
Given a device "plc-4" registered in fab "munich"
When a munich operator sends DELETE /kiosks/plc-4?fabId=munich
Then the response is 404 with title "KIOSK_NOT_FOUND"
  And the kind check, not the fab check, is what refuses it
```

## 5. Independent end-to-end test procedure

Run against the **already-running** Aspire stack (pid 3312 — do not stop it).
No test project required; this is the procedure a reviewer can repeat to see the
defect and then its absence.

1. Mint a munich token and a dresden token from Aspire's **proxied** Keycloak
   endpoint (not the container's mapped port — a mismatched issuer 401s
   everything):

   ```sh
   # management-web carries sse.identity.kiosks.write as a default scope and
   # enables the direct access grant; smart-sentinel-eye-web carries
   # sse.management, which the legacy bundle accepts for the same policy.
   curl -sk -d grant_type=password -d client_id=management-web \
        -d username=admin@munich.test -d password=Admin1234 \
        "$KC/realms/smart-sentinel-eye/protocol/openid-connect/token"
   curl -sk -d grant_type=password -d client_id=management-web \
        -d username=op-dresden@dresden.test -d password=Operator1234 \
        "$KC/realms/smart-sentinel-eye/protocol/openid-connect/token"
   ```

2. As munich, enrol a throwaway kiosk:
   `POST $IDENTITY/kiosks/enroll?fabId=munich` with `{"clientId":"wall-<guid>"}`
   → expect `201`.
3. **Before the fix**, as dresden: `DELETE $IDENTITY/kiosks/wall-<guid>`
   → observe **200**, and confirm in Keycloak that the client is now
   `enabled: false`. *That is the vulnerability, observed rather than argued.*
4. **After the fix**, repeat with `?fabId=munich` → **403
   RESOURCE_FAB_NOT_AUTHORIZED**; and with `?fabId=dresden` → **404
   KIOSK_NOT_FOUND**. Confirm in Keycloak that the client is still enabled.
5. As munich, `DELETE …?fabId=munich` → **200**, client now disabled.
6. Repeat 2-5 for `/devices` with `POST /devices/register?fabId=munich`.

Realm facts this rests on, read from
`src/AppHost/Realms/smart-sentinel-eye-realm.json`: groups `/fabs/munich`,
`/fabs/dresden`, `/fabs/berlin`, `/fabs/hamburg` exist; `admin@munich.test` holds
munich only; `op-dresden@dresden.test` holds dresden only; `management-web`
carries all four `sse.identity.*` scopes plus `sse-groups` as default scopes and
enables the direct access grant.

## 6. Locked tech choices (no new ones)

| Concern | Choice | Source |
|---|---|---|
| Fab authorization | `IFabAuthorizationGuard.EnsureAccessAsync`, DI-injected at the endpoint | existing; 37 endpoint-level call sites across 8 contexts |
| Fab named | **explicit `?fabId=`, required** — no inference | ADR-0114 scopes inference to Automation's rule endpoints |
| Cross-fab target | fab in the **lookup predicate**, answered 404 | `ICameraRepository.GetWithinFabAsync` + `RetireCameraCommandHandler:23` |
| Errors | `Result<T, Error>` + the existing `*NotFound` variants | ADR-0047, ADR-0089 |
| Guards | `Ensure.That(...)` | ADR-0105 |
| Tests | xUnit + Shouldly + Moq; integration via `AspireFixture` | ADR-0052, ADR-0103 |

## 7. Assumptions, marked

- **A1 — `fabId` is required, not inferred.** `IFabAuthorizationGuard`'s own doc
  comment says *"everywhere else an explicit `fabId` is required, and extending
  inference is a new decision rather than an application of that one."* Adopting
  `FabResolution.ResolveForWriteAsync` in Identity would be that new decision,
  and the autonomous lane may not make decisions. The sibling routes in the same
  two files (`POST /kiosks/enroll`, `POST /devices/register`) already take a
  required `fabId` and call the guard directly; this mirrors them exactly.
- **A2 — making `fabId` required breaks no caller.** Measured, not assumed:
  `grep` over `apps/` and the Playwright specs finds **zero** callers of either
  route — no RTK Query endpoint, no e2e spec. The only callers are backend tests.
- **A3 — the response body is unchanged** (`200` + the identifier). Narrowing it
  is not this issue.

## 8. Success criteria

- **SC-001** — AS-2 and AS-3 both refuse, over real HTTP against the real stack,
  with the victim's Keycloak client observed still enabled afterwards.
- **SC-002** — the AS-2/AS-3 integration test is observed **failing** against
  unfixed code, with the failure quoted verbatim in the PR (ADR-0139).
- **SC-003** — AS-1 and AS-6 still pass; `Architecture.Tests` stays green,
  including `ConcurrencyConflictDeclarationTests`, whose route register names
  both routes by path.
- **SC-004** — the two `// …because it runs no fab guard` comments no longer
  claim something false.
