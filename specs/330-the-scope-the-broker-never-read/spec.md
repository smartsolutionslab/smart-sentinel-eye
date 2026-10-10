# Spec 330 — The scope the broker never read

**Issue:** #2286 — "sse.events.publish is catalogued, granted to every device, and enforced by nothing"
**Branch:** `fix/2286-mqtt-publish-scope-enforcement` · **Worktree:** `D:\Github\sse-2286`
**Lane:** autonomous (ADR-0144). **The direction was decided by a human.** Issue comment,
2026-10-10: *"enforce it — the MQTT plugin starts checking scope for a publish, closing the gap by
making the catalogued control real (rather than removing the scope as never-enforced)."* This spec
works out how to enforce it. It does not re-open the choice.
**Related:** #2070 (the same shape: a scope catalogued and enforced nowhere), spec 008 (Identity,
device enrolment), spec 090 (the broker's `aud` check, which is the pattern this spec copies).
**ADRs:** ADR-0100 (Mosquitto JWT auth; its Decision already says a publish is allowed only if
*"the `scope` claim contains `sse.events.publish`"*), ADR-0095 (spec 006, `acl.txt` /
`password_file`), ADR-0111 (dev-only `scenario-simulator`), ADR-0036 (smallest change), ADR-0037
(phases), ADR-0103 (Aspire fixture, no Testcontainers), ADR-0109 (`[P]` = disjoint files),
ADR-0139 / ADR-0144 (red first, lane limits).
**Constitution:** §VIII (safe by default at trust boundaries), §IV (latency, see below),
§Testing (new behaviour starts red).
**New ADR needed:** no (plan §1).
**Latency budget:** touches the MQTT ingress hop that feeds the *Event → overlay state ≤ 200 ms*
leg. It adds one in-memory lookup per PUBLISH, with no network and no cryptography. That cost is
cited by measurement in phase 5 (plan §6). No other leg is touched.

---

## Problem (re-verified at `b97afeaa`, origin/develop)

| Claim in the issue | Today |
|---|---|
| The catalogue defines `sse.events.publish` | **Holds.** `src/ServiceDefaults/Authorization/Scope.cs:64`, realm client scope at `smart-sentinel-eye-realm.json:54` (`include.in.token.scope: true`). |
| `RequireScopeExtensions.cs:46` special-cases it as the one scope the management bundle may not satisfy | **Stale.** The `sse.management` bundle was withdrawn (spec 265 / #2486), so the special case is gone. `AddScopePolicies` is a plain ordinal match on space-split `scope` tokens. This changes nothing about the finding. |
| `KeycloakScopeBundles.cs:59` grants it to every registered device | **Holds**, now at `:72-76` (`Device` bundle). No *static* realm client holds it. `ScopeGrantTests.cs:66` says so in its own comment. |
| The plugin never reads `scope` | **Holds.** `src/AppHost/mosquitto/plugin/jwt_auth.go:146-194`. The plugin registers **only** `MOSQ_EVT_BASIC_AUTH` (`mosquitto_glue.c:37-41`). It has no ACL callback at all, so it never sees a PUBLISH. |
| Topic authority is decided only by `acl.txt` | **Holds.** Four users. A registered device has no row, so it can publish nothing whatever its scope. |
| `iss` is a suffix match (`jwt_auth.go:191`) | **Holds.** Out of scope here (§Out of scope 1, with the reason). |

### What this investigation established

1. **Publish authorisation is a separate Mosquitto event.** The callback that sees a PUBLISH is
   `MOSQ_EVT_ACL_CHECK`, not basic auth. Enforcing the scope means registering a second callback.
   The token exists only at CONNECT, so the plugin has to remember the verdict between the two
   events.
2. **The plugin's ACL callback runs *before* `acl.txt`, and the first non-defer answer wins.**
   This was read from the pinned 2.0.18 source, not assumed:
   - `src/mosquitto.c:538-540`: `mosquitto_security_module_init()` (plugin init) runs before
     `mosquitto_security_init()`.
   - `src/security_default.c:121`: the `acl_file` checker registers itself as an
     `MOSQ_EVT_ACL_CHECK` callback during `mosquitto_security_init`.
   - `src/plugin.c:278`: callbacks are `DL_APPEND`ed.
   - `src/security.c:720-737`: the callbacks are walked in order, and the first result that is not
     `MOSQ_ERR_PLUGIN_DEFER` is returned.

   **Consequence:** the plugin must answer `MOSQ_ERR_PLUGIN_DEFER` whenever it does not refuse.
   If it answered `SUCCESS`, it would bypass `acl.txt` entirely and *widen* authority. The scope
   is necessary, not sufficient.
3. **A client's Will is published *after* the disconnect event fires.** In
   `src/context.c:214-224`, `context__disconnect` calls `plugin__handle_disconnect`, then
   `context__send_will`. `context__send_will` runs an `MOSQ_ACL_WRITE` check (`:180-191`). A
   per-connection verdict deleted on disconnect would therefore be gone exactly when the Will
   needs it. The check would then defer to `acl.txt` alone, which fails open. Plan §3 records the
   keying this forces.
4. **One static client already publishes with a JWT and lacks the scope.** That client is
   `scenario-simulator` (realm `:335-358`). Every `tests/Integration.Tests/EventIngestion/*`
   class that publishes, plus `PlantFloor`, presents its token (`RealmImportMirrorTests`
   T1–T11). Enforcement without granting it the scope would silently stop the dev simulator and
   about twelve integration classes. Their failures would look like "the event did not arrive".
   The `event-ingestion` subscriber also authenticates by JWT without the scope, which is correct:
   it only reads.
5. **MQTT 5 makes a refusal observable.** A denied QoS 1 PUBLISH gets PUBACK reason
   `0x87 Not authorized` (`src/handle_publish.c:248-254`). MQTT 3.1.1 has no reason code, so the
   message is dropped silently. That is Mosquitto's standard ACL behaviour today, and this spec
   does not change it.
6. **No Go toolchain exists on a developer machine or in CI.** No workflow runs `go`. The plugin
   is compiled only inside `src/AppHost/mosquitto/Dockerfile` stage 2, which Aspire builds on
   every boot (`AppHost.cs:285`). That makes the image build the one place a Go test is
   guaranteed to run (plan §5).

## Decision encoded by this spec

| Item | Value |
|---|---|
| Where the scope is read | At CONNECT, in `sseOnBasicAuth`, after every existing check has passed |
| What is required | `sse.events.publish` as an exact, ordinal, space-delimited token in the `scope` claim. The rule mirrors `RequireScopeExtensions.AddScopePolicies`. |
| Where it is enforced | A new `MOSQ_EVT_ACL_CHECK` callback, for `MOSQ_ACL_WRITE` only |
| Verdicts | JWT identity **without** the scope, write → `MOSQ_ERR_ACL_DENIED`. Every other case → `MOSQ_ERR_PLUGIN_DEFER`, so `acl.txt` still decides. |
| Unaffected | CONNECT (a JWT without the scope still connects), READ / SUBSCRIBE / UNSUBSCRIBE, password-file users |
| Realm | `scenario-simulator` gains `sse.events.publish` as a default client scope |

## Out of scope (each with its reason)

1. **The issuer suffix match.** An exact `iss` check needs the issuer the *clients* mint
   against. The broker knows only its JWKS URI, which is the container-network Keycloak address
   (`AppHost.cs:293`). Keycloak has no `KC_HOSTNAME` (`AppHost.cs:150`), so it derives `iss`
   from the request host: tests and host-run services mint through Aspire's proxied endpoint, the
   broker fetches keys from another address, and the two issuers differ. Tightening the check is
   therefore a new configuration input, or a Keycloak hostname decision, with a real risk of
   refusing every MQTT client. It is not the one-line change the issue suggests. The signature
   must still verify against the realm JWKS, so the suffix match is defence in depth only.
   **Filed as a follow-up at phase 7** (plan §7 has the draft).
2. **ADR-0100's other two publish conditions**: `groups` contains `/fabs/<fabId>`, and `azp`
   matches the topic's device segment. They remain unbuilt. `acl.txt` stands in for both, as it
   does today.
3. **Registered devices still have no `acl.txt` row**, so they still cannot publish anything.
   Giving them topic authority (an ACL pattern or a dynamic ACL) widens authority. It is a
   separate decision, not part of making a catalogued control real.
4. **Exact-issuer or scope checks for the WHEP hook or the HTTP APIs.** Those already enforce
   their own scopes through `RequireScope`.

---

## User stories

### US1 (P1, the whole slice): a publish needs the publish scope

**As** the operator of a fab whose broker accepts any realm-signed token with the right audience,
**I want** the broker to refuse a PUBLISH from an identity whose token lacks `sse.events.publish`,
**so that** the scope the catalogue and the device bundle advertise does the work they claim, and
a leaked non-device credential (e.g. `scenario-simulator`'s, in the tree) cannot publish just
because `acl.txt` names its user.

It ships independently: one plugin change, one realm grant and one probe ACL row. It can be
observed end to end by publishing to the broker with and without the scope.

## Acceptance scenarios

Live facts run against the Aspire stack (ADR-0103). File-level facts run in
`Architecture.Tests`. Unit facts run in Go inside the image build (plan §5).

**AS-1: the scope is required for a publish (happy path / auth, red today).**
```gherkin
Given a realm client "mqtt-publish-scope-probe" whose default scopes are sse-audience only
  And acl.txt grants user "mqtt-publish-scope-probe" write on "sse-probe/publish-scope"
  And its client_credentials token names smart-sentinel-eye-api in aud, names the probe in azp,
      and does not carry sse.events.publish in scope
 When it connects over MQTT 5 with that token as its password
 Then the CONNACK is Success
 When it publishes at QoS 1 to "sse-probe/publish-scope"
 Then the PUBACK reason code is NotAuthorized
```

**AS-2: the same identity with the scope publishes (positive control, green before and after).**
```gherkin
Given the same probe client, recreated with default scopes sse-audience and sse.events.publish
 When it connects over MQTT 5 and publishes at QoS 1 to "sse-probe/publish-scope"
 Then the PUBACK reason code is Success
```
AS-1 and AS-2 differ **only** in the scope. The username, topic, ACL row and audience are the
same, so a fix that refuses everything cannot pass AS-2, and an ACL refusal cannot pass for AS-1.

**AS-3: the scope does not widen authority (conflict).**
```gherkin
Given a registered device (POST /devices/register), whose token carries sse.events.publish
  And acl.txt has no row for its username
 When it publishes at QoS 1 to "fab/munich/plc/<its device id>"
 Then the PUBACK reason code is NotAuthorized
```
(Green before and after. It pins finding 2: the plugin defers rather than grants.)

**AS-4: every static realm client with an ACL write grant holds the scope (configuration, red today).**
```gherkin
Given src/AppHost/mosquitto/acl.txt and src/AppHost/Realms/smart-sentinel-eye-realm.json
 Then every acl.txt user that has a "topic write" line and is also a realm client
      lists sse.events.publish in its defaultClientScopes
```
Today `scenario-simulator` fails this, which is finding 4. The guard turns "the simulator
silently stopped publishing" into a build failure that names the client.

**AS-5: scope parsing is exact (bad request, Go unit).**
```gherkin
Given a scope claim
 Then "openid sse.events.publish profile" grants publish
  And "sse.events.publish" grants publish
  And "sse.events.publisher", "xsse.events.publish", "SSE.EVENTS.PUBLISH", "" , a missing claim,
      and a JSON array claim do not
```

**AS-6: the verdict table (Go unit).**
```gherkin
Given the per-identity grant memory
 Then a write by an identity last authenticated without the scope is denied
  And a write by an identity last authenticated with the scope defers
  And a write by an identity never seen by the JWT path (a password-file user) defers
  And a read, subscribe or unsubscribe defers for every identity
  And the most recent authentication for an identity replaces the earlier verdict, in both directions
```

**AS-7: no regression.**
```gherkin
Given a fresh keycloak-data volume and a rebuilt mosquitto image
 When the full Integration.Tests suite (all four shards) runs
 Then it is green, including every EventIngestion class that publishes as scenario-simulator
  And MqttAudienceIntegrationTests and NFR002_MqttConnectAuthTests pass unmodified
```

## Independent end-to-end test procedure

1. Remove the `keycloak-data` volume, because a realm edit is not re-imported otherwise. Remove
   any persistent `mosquitto` container with `docker rm`, keeping its volume, because a
   persistent container keeps its old image. Boot the AppHost.
2. Confirm that the image you are testing is the new one. The mosquitto log must show the
   plugin's new connect-time line for `event-ingestion`: *"authenticated without
   sse.events.publish"*.
3. Mint a `scenario-simulator` token and decode it. `scope` contains `sse.events.publish`.
   Confirm that simulated events still reach EventIngestion (e.g. `PlantFloor`-driven test, or
   ingest counters on the dashboard).
4. Run `MqttPublishScopeIntegrationTests` (AS-1..3). Quote the PUBACK reason codes. Confirm the
   mosquitto log shows `Denied PUBLISH from mqtt-publish-scope-probe-…` for AS-1 only.
5. Run `IngestThroughputMeasurementTests` and `NFR002_MqttConnectAuthTests` **twice each**
   (project memory: the first run after churn looks like a regression). Quote the figures against
   the develop baseline.

## Edge cases (decided)

| Case | Behaviour | Why |
|---|---|---|
| Will message from a JWT client without the scope | Refused | The verdict outlives the connection (finding 3, plan §3). |
| Delayed Will (MQTT 5 `will_delay`) | Refused | Same reason. The username stays set on the context until session expiry. |
| JWT client without the scope, MQTT 3.1.1, QoS 0/1 | Dropped silently | Protocol limitation, and identical to an ACL refusal today. |
| `$SYS/…` and other `$` topics | Unchanged | `acl__check_dollar` runs before any callback. |
| Bridge connections | Unchanged | `mosquitto_acl_check` returns success for bridges before any callback. |
| ACL check with no username on the context | Defer | No identity to look up. Anonymous access is disabled, so this is reachable only during teardown, after the Will has already been handled. |
| Password-file user | Defer | Unchanged. `passwords.txt` holds no hashes today. |
| Password-file login using a name that previously authenticated by JWT without the scope | Writes refused | Fail closed: the same name is the same identity (plan §3). |
| Panic inside the ACL callback | `MOSQ_ERR_ACL_DENIED` | Fail closed. It mirrors basic auth's `recover` → `MOSQ_ERR_AUTH`. |
| JWKS not yet loaded | Unchanged | CONNECT is refused before any PUBLISH. |

## Assumptions (marked)

- **A1 (verified from source; proven live by AS-1/AS-2):** the Mosquitto 2.0.18 dispatch order
  in finding 2.
- **A2 (checked at 4b):** a Keycloak client's `sse.events.publish` is a *default* client scope
  wherever it is granted (realm and `KeycloakScopeBundles.Device`). No client takes it as an
  optional scope. So every token one client mints carries the same answer, and remembering the
  verdict per username is faithful (plan §3). If an optional grant is ever introduced, the most
  recent token wins for all of that client's sessions. Plan §3 states the consequence.
- **A3:** the plugin's per-username memory grows with the number of distinct JWT identities that
  have connected since broker start. That is the fleet plus test throwaways. At the 250-camera
  target this is kilobytes.
