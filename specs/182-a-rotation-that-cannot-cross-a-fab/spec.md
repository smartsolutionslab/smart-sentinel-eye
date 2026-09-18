# Spec 182 — A rotation that cannot cross a fab

**Issue:** [#2280](https://github.com/smartsolutionslab/smart-sentinel-eye/issues/2280)
— on Project #13, status **Todo**, label `agent:ready`. Verified 2026-09-18 by
`content.url` against a `--limit 2000` dump, not by the number filter.
**No `item-add` needed.**

**Spec number.** `git ls-tree -d origin/develop -- specs/` shows **180** as the
highest merged. **181 is claimed by an unmerged sibling PR (#2454, issue #2268)**,
so this spec is **182**. The number one above develop's highest is not free; it
has collided twice this session.

**ADRs referenced:** ADR-0114 (fab resolution, and the guard's declared scope),
ADR-0041 (repository contract), ADR-0040 / ADR-0073 (integration events carry
primitives at the wire boundary; no cross-context project reference),
ADR-0047 / ADR-0089 (`Result<T, Error>` + `ApiError`), ADR-0105 (`Ensure.That`),
ADR-0113 (two-layer optimistic concurrency — the If-Match/If-None-Match upsert
precondition this endpoint already enforces), ADR-0142 (the idempotency key on
this route), ADR-0139 + constitution §Testing (observed red), ADR-0103
(integration via the Aspire fixture), ADR-0036 (smallest change), ADR-0037 /
ADR-0144 (the lane), ADR-0109 (the `[P]` disjoint-file rule).

**Constitution:** §VIII — *"Authorization is enforced by scope checks at every
endpoint, plus fab-group membership."* The rotation route has both and is still
crossable, because the membership check and the lookup are checking different
things. This spec does not amend §VIII; it makes it true of this route.

**Latency budget (§IV): N/A.** Identity is an administrative write path, and the
EventIngestion change is on the webhook *registration* path, not the ingest path.
Nothing here touches any of the six legs of `event arrival → overlay rendered`.

---

## 1. The defect, confirmed by content rather than by line number

The issue cites lines from 2026-09-13. Re-confirmed against `develop`
(`cab037f0`) on 2026-09-18 — the line numbers have drifted slightly, the code has
not:

- `src/Identity/Application/Commands/Handlers/RotateWebhookClientCommandHandler.cs:39-53`
  destructures `fab` from the command, builds `ClientId.From($"webhook-{integrationName}")`,
  and calls **`clients.GetByClientIdAsync(clientId, …)`** — the unscoped lookup.
  `fab` is used afterwards only to *write* (the Keycloak representation's
  `sse.fab` attribute, the fab group path, `RegisteredClient.Register`, the
  published metadata, the response DTO). **It is never compared to
  `existing.Value.Fab`.** Grep confirms: `Fab` appears in that file only on the
  deconstruction and on those write sites.
- `src/Identity/Api/WebhookRotationEndpoints.cs:118-133` parses the fab from
  `body.FabId` and calls `fabGuard.EnsureAccessAsync(user, fab.Value, …)` before
  the precondition is read. **The guard is present and correct.** It proves the
  caller holds the fab they typed. It cannot prove the *client* is in it.
- `src/EventIngestion/Application/EventHandlers/WebhookIntegrationRotatedV1Handler.cs:21-42`
  deconstructs `(integrationName, clientId, _, _)` — discarding the metadata that
  carries the fab — and calls **`integrations.GetByNameAsync(name, …)`**, then
  `MarkAsRotated`. No fab is read at all.

**The precedent that is already in the repo.** Spec 180 (#2240, merged) added
`IRegisteredClientRepository.GetWithinFabAsync(fab, clientId, ct)` with the fab
**in the predicate** (`RegisteredClientRepository.cs:39-58`) and used it from
`DisableKioskCommandHandler.cs:24` and `DisableDeviceCommandHandler.cs:24`. The
method this spec needs on the Identity side **already exists and is unused by
this handler.** The doc comment on it names the reason verbatim: *"a client
enrolled in a different fab is never materialised, so a caller cannot disable it
by naming a fab it does not belong to."*

**Why a guard alone is not the fix.** `EnsureAccessAsync(user, "munich")` succeeds
for a caller who genuinely holds munich. The attack that survives a bare guard is
the one where the attacker names **their own** fab — spec 180's AS-3 — and an
unscoped lookup then finds and mutates the victim's row anyway. That is the
shape here too.

### 1.1 Reachability

`RequireScopeExtensions.LegacyManagementBundle` makes `sse.management` satisfy
every `sse.*` policy except `sse.events.publish`, and `smart-sentinel-eye-web`
carries `sse.management` as a default client scope. So every seeded operator can
already call `POST /webhook-integrations/{name}/rotate`. The attacker needs no
specially-scoped principal. (That the bundle is too broad is a **separate** issue
— see §3.)

### 1.2 The two attacks, and why they are two

**Attack A — the integration has already been rotated.** Dresden's
`webhook-dresden-line-3` exists as a `RegisteredClient` row in fab `dresden` and
as a Keycloak client. A munich operator sends
`POST /webhook-integrations/dresden-line-3/rotate`, body `{"fabId":"munich"}`,
`If-Match: "7"`. The guard passes honestly. `GetByClientIdAsync` finds Dresden's
row across the fab boundary, the version matches, `Rotate` runs, Keycloak rolls
the secret, and **Dresden's new client secret is returned in the response body**.
Dresden's live credential is dead. One request, disclosure plus destruction.

**Attack B — the integration has never been rotated** (still spec-006 legacy
static-hash mode; no `RegisteredClient` row, no Keycloak client). A munich
operator sends the same route with `If-None-Match: *`. The create branch mints
Keycloak client `webhook-dresden-line-3` **in `/fabs/munich`** with
`sse.fab=munich`, writes a munich `RegisteredClient` row, and publishes
`WebhookIntegrationRotatedV1("dresden-line-3", "webhook-dresden-line-3", …)`.
EventIngestion resolves that **by name**, finds Dresden's integration, and flips
it to `BearerValidationMode.Jwt` pointing at **the attacker's** Keycloak client.
Dresden's legitimate static bearer stops being accepted, and tokens minted from
the munich-held client are.

**Attack B is why this spec has two halves.** Scoping the Identity lookup closes
A completely and closes *nothing* of B: on a first rotation there is genuinely no
row in any fab to scope against, so `GetWithinFabAsync(munich, …)` correctly
returns `None` and the create branch is correctly taken. **The Identity fix alone
would ship a spec that closes the theft and leaves the takeover live.**

## 2. Does `WebhookIntegrationRotatedV1Handler` need its own fix? — **Yes.**

Stated explicitly because the brief asked for the conclusion rather than a silent
scoping decision.

It is **not** defence-in-depth and **not** adequately covered by fixing the write
path. It is the second half of one vulnerability:

- The write-path fix cannot see attack B. Identity holds no record of who owns a
  webhook integration name until the first rotation creates one, and it **may not
  ask** — `src/EventIngestion/Domain/WebhookIntegration/` is another bounded
  context and a project reference to it is forbidden (house rule, NetArchTest).
  So **EventIngestion is the only place in the system that knows the integration's
  fab at the moment the flip is applied.** The check has to live there.
- The name space makes the collision reachable rather than theoretical.
  `ux_webhook_integrations_name` is **globally unique, deliberately** — the
  configuration says so: *"Still globally unique, not (fab, name): the name is the
  path segment of `POST /events/webhook/{name}`"*. One global name space shared by
  every fab, resolved with no fab, is precisely the condition under which
  resolve-by-name crosses a tenancy boundary.
- The event already carries everything the check needs.
  `EventMetadata.Fab` is populated by the publisher (`RotateWebhookClientCommandHandler`
  passes `fab.Value`), so this is a comparison against data in hand, not a new
  field on a contract and not a `V2` (ADR-0073).

**What it is not.** It is not a claim that the RabbitMQ bus is an untrusted
boundary and it does not attempt to defend against an attacker who can publish
arbitrary messages onto it — such an attacker is already inside. The check is
required because a **legitimately published** message, produced by the code path
above, carries a fab that does not match the integration it names.

## 3. Scope — and what is deliberately not in it

**In scope:** the rotate branch's lookup (Identity) and the rotation subscriber's
lookup (EventIngestion).

**Out of scope, explicitly, no re-audit and no folding in:**

- **#2281, the list leak** — `GET /webhook-integrations` with `fabId` omitted
  returns every fab's clients. `WebhookRotationEndpoints.List` and
  `ListWebhookClientsQueryHandler` are on the forbidden list in `tasks.md`. This
  mirrors spec 180's own exclusion of the three `List` handlers. The fix here does
  not need it and does not touch it. Note the honest consequence: while #2281 is
  open, the attacker still has an easy way to read another fab's integration names
  and versions. **This spec removes what those values buy, not the reading of
  them.**
- **The console-bundle issue** — every operator holding `sse.webhooks.write`
  through `LegacyManagementBundle`. A broader authorization-model change, and an
  ADR-level decision. Not touched.
- **`EnrollKioskCommandHandler:25` and `RegisterDeviceCommandHandler:42`** also
  call `GetByClientIdAsync` unscoped. They are **create** paths on a
  caller-supplied `clientId`, so the unscoped lookup is what makes "already
  registered" correct across the global name space; it discloses existence, not a
  credential. Same *method*, different *defect class*. Out of scope; noted so a
  reviewer does not read their absence as an oversight.

### 3.1 A residual this spec does not close — **flagged, not silently scoped out**

After both halves land, attack B's *first move* still succeeds in part. A munich
operator can send `If-None-Match: *` naming an unrotated Dresden integration and
get a Keycloak client `webhook-dresden-line-3` in `/fabs/munich` plus a munich
`RegisteredClient` row. US2 makes that credential **useless** — EventIngestion
refuses the flip, Dresden keeps validating on its static hash, nothing is
disclosed and nothing is destroyed. But the global name is now **squatted**:
Dresden can no longer perform their own first rotation, because
`CreateClientAsync`'s existence probe
(`HttpKeycloakAdminClient.cs:57-62`) will throw `KeycloakClientAlreadyExistsException`
→ `KEYCLOAK_UNAVAILABLE` (502), and `If-Match` finds nothing in `dresden` → 412.

Severity drops from *credential takeover* to *denial of one administrative
operation, plus litter an admin can delete*. Closing it requires Identity to learn
which fab owns a webhook integration name before minting anything — which needs
either a cross-context query or ownership moved into `Shared.Contracts`. **Both
are architecture decisions, and the autonomous lane may not make one**
(ADR-0144). **Recommendation: file a follow-up issue and an ADR.** It is recorded
here rather than fixed, so the next reader meets it instead of discovering it.

## 4. User stories

Both are **P1**, and they are **one slice**. Shipping US1 without US2 would
announce "cross-fab webhook rotation is fixed" while leaving a complete cross-fab
takeover live, which is worse than shipping neither — the same reasoning by which
spec 180 refused to split its two disable routes. They touch **disjoint files in
different bounded contexts**, so they are built in parallel (`[P]`, ADR-0109), not
in sequence.

### US1 (P1) — A rotation is confined to a fab the caller holds

*As an operator holding `sse.webhooks.write` in one fab, I can rotate my own
fab's webhook integrations and cannot roll, read, or invalidate another fab's
credential — whether I name their fab or my own.*

Independently observable: two HTTP calls against the running stack show the
refusal, one shows the legitimate rotation still working, and the victim's
Keycloak secret is read back unchanged.

### US2 (P1) — A rotation announcement only flips an integration in its own fab

*As the operator of a fab, an integration of mine is never switched to
JWT validation by a rotation performed in another fab.*

Independently observable: publish the rotation (via a munich-scoped first
rotation naming a dresden integration) and read the integration's validation mode
back — it stays `StaticHash` and its existing bearer keeps working.

**There is no P2.**

## 5. Acceptance scenarios (Gherkin)

Fixture for every scenario: integration `dresden-line-3` is registered in fab
`dresden`; the operator's token carries only `/fabs/munich` unless stated.

### AS-1 — happy path, own fab, subsequent rotation

```gherkin
Given a webhook integration "munich-line-1" in fab "munich"
  And a RegisteredClient "webhook-munich-line-1" in fab "munich" at version 4
  And an operator whose groups claim contains "/fabs/munich"
When they send POST /webhook-integrations/munich-line-1/rotate
  with body {"fabId":"munich"} and If-Match: "4"
Then the response is 200 OK carrying a new clientSecret and version 5
  And the Keycloak client's secret has changed
```

### AS-2 — happy path, own fab, first rotation

```gherkin
Given a webhook integration "munich-line-2" in fab "munich" in StaticHash mode
  And no RegisteredClient for "webhook-munich-line-2"
When a munich operator sends POST /webhook-integrations/munich-line-2/rotate
  with body {"fabId":"munich"} and If-None-Match: *
Then the response is 200 OK carrying a clientSecret
  And the integration's ValidationMode is Jwt
  And its KeycloakClientId is "webhook-munich-line-2"
```

### AS-3 — the cross-fab attack, naming the victim's fab *(already refused today)*

```gherkin
Given a RegisteredClient "webhook-dresden-line-3" in fab "dresden" at version 7
When a munich-only operator sends POST /webhook-integrations/dresden-line-3/rotate
  with body {"fabId":"dresden"} and If-Match: "7"
Then the response is 403 with title "RESOURCE_FAB_NOT_AUTHORIZED"
  And Dresden's Keycloak client secret is unchanged
```

This one the existing guard already covers. It is asserted so the fix is not
allowed to regress it.

### AS-4 — the cross-fab attack, naming the attacker's own fab **(the defect)**

```gherkin
Given a RegisteredClient "webhook-dresden-line-3" in fab "dresden" at version 7
When a munich-only operator sends POST /webhook-integrations/dresden-line-3/rotate
  with body {"fabId":"munich"} and If-Match: "7"
Then the response is 412 with title "WEBHOOK_CLIENT_NOT_FOUND"
  And no clientSecret appears anywhere in the response body
  And Dresden's Keycloak client secret is byte-for-byte unchanged
  And Dresden's registered_clients row is still at version 7
```

**412, not 403, and not a new error code.** The scoped lookup returns `None`, so
the handler takes the branch it already has for "you sent If-Match and nothing is
there" — `WebhookClientNotFound`, 412. That answer is **identical** to the one a
caller gets for a name that exists in no fab at all, which is the property that
matters: the route cannot be used to enumerate what another plant runs (the same
reasoning as `CameraEndpoints.cs:104-106`, and as spec 180's AS-3 404). No new
`ApiError` variant is introduced, and none should be — a distinct "wrong fab" code
would *be* the enumeration oracle.

### AS-5 — the cross-fab takeover via the create branch **(the defect US2 closes)**

```gherkin
Given a webhook integration "dresden-line-3" in fab "dresden" in StaticHash mode
  And no RegisteredClient for "webhook-dresden-line-3"
When a munich-only operator sends POST /webhook-integrations/dresden-line-3/rotate
  with body {"fabId":"munich"} and If-None-Match: *
Then the Dresden integration's ValidationMode is still StaticHash
  And its KeycloakClientId is still null
  And a bearer minted from the attacker's Keycloak client is rejected by
      POST /events/webhook/dresden-line-3
  And Dresden's original static bearer is still accepted
```

The HTTP status of the rotation call itself is **not** asserted here: after US1 the
Identity side legitimately believes it is creating a new client, and it is US2 that
refuses the effect. See §3.1 for the residual this leaves.

### AS-6 — the rotation announcement, unit level

```gherkin
Given a WebhookIntegrationRotatedV1 for "dresden-line-3"
  And its EventMetadata.Fab is "munich"
  And the integration "dresden-line-3" is registered in fab "dresden"
When the subscriber handles it
Then the integration is unchanged (StaticHash, KeycloakClientId null)
  And the refusal is logged with the integration name
```

```gherkin
Given a WebhookIntegrationRotatedV1 for "dresden-line-3"
  And its EventMetadata.Fab is null
When the subscriber handles it
Then the integration is unchanged
  And the refusal is logged
```

**Absent is refused, not waved through.** The handler mutates a security-relevant
validation mode, and a message with no fab is indistinguishable from one whose fab
was dropped. The real publisher always sets it (`fab.Value`, never null), so no
production path is affected — but the two existing unit tests construct their
metadata with `Fab: null` and will need that corrected to the integration's fab.
**That is a deliberate correction of test *data* to match what production sends,
declared here so it is not mistaken for an engineer bending a test to green**
(ADR-0144). Their assertions are untouched.

### AS-7 — bad request

```gherkin
Given a munich operator
When they send POST /webhook-integrations/x/rotate with body {"fabId":"  "}
Then the response is 400 with title "WEBHOOK_INVALID_INPUT"
  And nothing is created or rotated
```

### AS-8 — auth

```gherkin
Given a caller with no bearer token
When they send POST /webhook-integrations/munich-line-1/rotate
Then the response is 401
```

```gherkin
Given a caller authenticated without sse.webhooks.write and without sse.management
When they send POST /webhook-integrations/munich-line-1/rotate
Then the response is 403, refused by the scope policy before any fab is read
```

### AS-9 — unchanged behaviour that must stay unchanged

```gherkin
Given no webhook client "webhook-ghost" exists in any fab
When a munich operator rotates "ghost" with If-Match: "1"
Then the response is 412 with title "WEBHOOK_CLIENT_NOT_FOUND"
  And the body is indistinguishable from AS-4's
```

```gherkin
Given a RegisteredClient "webhook-munich-line-1" in fab "munich" at version 4
When a munich operator rotates it with If-Match: "2"
Then the response is 409 with title "WEBHOOK_CLIENT_STALE"
```

```gherkin
Given a munich operator has rotated "munich-line-1" with Idempotency-Key "k1"
When they repeat the identical request with the same key
Then the same secret and version are replayed (ADR-0142), not a 412
```

```gherkin
Given a RegisteredClient "webhook-munich-line-1" in fab "munich" at version 4
When a munich operator rotates it with If-None-Match: *
Then the response is 412 with title "WEBHOOK_CLIENT_ALREADY_EXISTS"
  And the branch-mismatch refusal is what produced it, not the fab check
```

## 6. Independent end-to-end test procedure

Run against a **freshly booted** Aspire stack. A persistent `dotnet run` keeps
serving the binaries it loaded at boot, so a stack predating this commit shows
pre-fix behaviour whatever is on disk — check the AppHost process's start time
against the commit first.

1. Mint tokens from Aspire's **proxied** Keycloak endpoint (not the container's
   mapped port — a mismatched issuer 401s everything):

   ```sh
   curl -sk -d grant_type=password -d client_id=management-web \
        -d username=admin@munich.test -d password=Admin1234 \
        "$KC/realms/smart-sentinel-eye/protocol/openid-connect/token"
   curl -sk -d grant_type=password -d client_id=management-web \
        -d username=op-dresden@dresden.test -d password=Operator1234 \
        "$KC/realms/smart-sentinel-eye/protocol/openid-connect/token"
   ```

2. As dresden, register a throwaway webhook integration in `dresden`
   (`POST $INGEST/webhook-integrations`), keeping the plaintext bearer it
   returns. Rotate it once as dresden (`If-None-Match: *`, `{"fabId":"dresden"}`)
   → `200`; note the version `N` from `GET /webhook-integrations`.
3. **Before the fix**, as munich:
   `POST $IDENTITY/webhook-integrations/<name>/rotate`, `{"fabId":"munich"}`,
   `If-Match: "N"` → observe **200 and a `clientSecret` in the body**, and confirm
   via the Keycloak Admin API that Dresden's client secret has changed. *That is
   the vulnerability, observed rather than argued.*
4. **After the fix**, repeat step 3 → **412 `WEBHOOK_CLIENT_NOT_FOUND`**, no
   secret in the body, Keycloak's secret unchanged. Repeat with
   `{"fabId":"dresden"}` → **403 `RESOURCE_FAB_NOT_AUTHORIZED`**.
5. As dresden, rotate with `{"fabId":"dresden"}` and `If-Match: "N"` → **200**,
   new secret. The legitimate path still works.
6. **US2.** Register a second dresden integration and do **not** rotate it. As
   munich, rotate it with `If-None-Match: *`, `{"fabId":"munich"}`. Then read the
   integration back: `validation_mode` is still `StaticHash`,
   `keycloak_client_id` is still null. Post an event to
   `POST $INGEST/events/webhook/<name>` with the original static bearer →
   accepted; with a token minted from the munich client → rejected.

Realm facts this rests on, from `src/AppHost/Realms/smart-sentinel-eye-realm.json`:
groups `/fabs/munich`, `/fabs/dresden`, `/fabs/berlin`, `/fabs/hamburg` exist;
`admin@munich.test` holds munich only; `op-dresden@dresden.test` holds dresden
only; `management-web` enables the direct access grant and the legacy
`sse.management` bundle satisfies `sse.webhooks.write`.

## 7. Locked tech choices (no new ones)

| Concern | Choice | Source |
|---|---|---|
| Fab authorization at the endpoint | `IFabAuthorizationGuard.EnsureAccessAsync` — **already present**, unchanged | ADR-0114; `WebhookRotationEndpoints.cs:130` |
| Fab named by the caller | explicit, required `body.FabId` — **already present**, unchanged | ADR-0114 (inference is scoped to Automation) |
| Cross-fab target, Identity | fab in the **lookup predicate** via the existing `IRegisteredClientRepository.GetWithinFabAsync` | spec 180; `RegisteredClientRepository.cs:39` |
| Cross-fab target, EventIngestion | fab in the **lookup predicate** via a new `IWebhookIntegrationRepository.GetWithinFabAsync` | same pattern; ADR-0041 |
| Fab on the wire | existing `EventMetadata.Fab` (`string?`) — no contract change, no `V2` | ADR-0040, ADR-0073, ADR-0102 |
| Errors | the **existing** `WebhookClientNotFound` variant; no new code | ADR-0047, ADR-0089 |
| Guards | `Ensure.That(...)` | ADR-0105 |
| Logging | `[LoggerMessage]` in `EventIngestion/Application/Log.cs`, beside `RotationTargetMissing` | ADR-0050 |
| Tests | xUnit + Shouldly + Moq + hand-written fakes; integration via `AspireFixture` | ADR-0052, ADR-0103 |

**Why `GetWithinFabAsync` and not `LayoutComposition`'s `GetByIdentifierAsync(fabs, …)`.**
Both precedents put the fab in the predicate, which is the property that matters.
`PublishRevisionCommandHandler:22` passes a **list** of fabs because its command
carries the caller's whole fab set. Both routes here carry exactly one fab — the
endpoint has already narrowed it and the guard has already proved it — so the
single-fab overload is the right fit, and it is the one this context already owns
and tests. Defaulting to spec 180 as the brief directs; the fit is genuine, not
just recency.

## 8. Assumptions, marked

- **A1 — the endpoint needs no change at all.** `body.FabId` is required
  (`RotateWebhookClientRequest(string FabId)`, non-nullable) and
  `EnsureAccessAsync` already runs before the precondition is read, with a comment
  explaining the ordering. Unlike spec 180, there is no missing guard here. The
  defect is entirely in the Application layer. Verified by reading, not assumed.
- **A2 — no `ApiError` variant is added and no status changes for any legitimate
  caller.** AS-4 reuses the 412 that AS-9's unknown-name case already produces.
  A caller who was previously served has no observable change.
- **A3 — `WebhookIntegrationName` is globally unique and stays so.** Confirmed
  from `WebhookIntegrationConfiguration.cs:80-85` and its comment. This spec does
  **not** propose making it `(fab, name)` — that would change the public ingest
  route's shape and is an ADR-level decision.
- **A4 — no frontend caller exists.** `grep` over `apps/` and the Playwright specs
  for `webhook-integrations` finds no RTK Query endpoint and no e2e spec for the
  rotate route; the callers are backend tests and curl. To be re-measured by the
  engineer before relying on it.
- **A5 — `EventMetadata.Fab` is always populated on this event by the only
  publisher.** `RotateWebhookClientCommandHandler` passes `fab.Value`, which is
  non-null by construction. The null branch in US2 therefore guards against a
  future or faulty publisher, and costs one `if`.

## 9. Success criteria

- **SC-001** — AS-4 refuses over real HTTP against the real stack, with the
  victim's Keycloak client secret read back **unchanged** afterwards. Reading our
  own `registered_clients` row alone is not sufficient evidence; spec 121 (#2165 /
  #2207) is the proof that our row and Keycloak can disagree.
- **SC-002** — AS-5's integration test observes the Dresden integration still in
  `StaticHash` mode with `KeycloakClientId` null after a munich first-rotation.
- **SC-003** — the AS-4 and AS-5 tests are observed **failing against unfixed
  code**, and the failure is quoted verbatim in the PR (ADR-0139). For AS-4 the red
  must show a **200 with a `clientSecret`**, not a mismatch on a status the
  endpoint never produced. For AS-5 it must show `ValidationMode == Jwt`.
- **SC-004** — AS-1, AS-2, AS-3, AS-7, AS-8, AS-9 all pass, including the
  idempotency-replay case; `Architecture.Tests` stays green, notably
  `ConcurrencyConflictDeclarationTests` (which registers this route by path),
  `HandlerDeconstructionTests` (the EventIngestion handler's deconstruction gains
  a named `metadata` local), `EventMetadataFabDeclarationTests`, and
  `PrimitiveBoundaryTests`.
- **SC-005** — §3.1's residual is recorded in the PR body as a known, deliberate
  non-closure with its severity stated, and a follow-up is recommended to the
  orchestrator rather than filed by the lane.
