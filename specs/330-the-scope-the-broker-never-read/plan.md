# Plan 330 — The scope the broker never read

**Spec:** [spec.md](spec.md) · **Issue:** #2286 · **Phase:** 2 (Plan)

**Phase-4a colour:** **red** (behaviour-changing). A PUBLISH that succeeds today is refused
after this change.
**Engineer:** `infra-engineer` (Go plugin, cgo glue, Dockerfile, `acl.txt`, realm JSON).
`test-writer` writes the C# facts and the Go unit tests. No backend or frontend engineer.
**Reviewers:** `infra-reviewer` + `security-reviewer` (a trust-boundary authorisation change in
the broker).

## 1. Context, boundaries, ADR position

- **Bounded context:** none. The change touches the AppHost-owned Mosquitto image
  (`src/AppHost/mosquitto/`), the realm import, and tests. No Domain, Application or Api code. No
  entity, value object, message, `Shared.Contracts` change or cross-context reference. §II
  (primitives) does not apply: no C# domain model changes.
- **No new ADR.** ADR-0100's Decision already prescribes this check: *"a publish … is allowed iff
  the `scope` claim contains `sse.events.publish`"*. The plugin that replaced `mosquitto-go-auth`
  (spec 008 addendum) implemented ADR-0100's authentication half and not this ACL half. This
  spec builds the missing half with the mechanism Mosquitto provides for it
  (`MOSQ_EVT_ACL_CHECK`). It adds no dependency (Go stdlib only for the new code; the existing
  `golang-jwt` / `keyfunc` pins are untouched), no new runtime component and no new kind of
  principal.
  - **A judgement call, stated so a reviewer can contest it:** ADR-0100's Context says
    per-message auth overhead "must remain zero". That sentence was written against per-message
    *introspection or crypto*: the same ADR's Decision has the plugin evaluate the scope per
    publish. Mosquitto already runs the `acl.txt` check on every PUBLISH. This adds one cgo
    crossing and one map read before it. Phase 5 measures it (§6). If a reviewer reads the
    sentence literally, the remedy is an ADR-0100 addendum by a human. The lane may not write it.
- **Lane limits respected:** no test deleted, no assertion edited, no threshold, suppression or
  analyzer change. The one comment that becomes false (`ScopeGrantTests.cs:66`, *"no static
  client holds it"*) is reworded, with no assertion change.

## 2. The plugin: what changes in `src/AppHost/mosquitto/plugin/`

### 2.1 New pure-Go package `authz/` (no cgo)

It is a separate package so it can be unit-tested with `CGO_ENABLED=0`. The `main` package
imports `<mosquitto.h>` through cgo, and a test binary built from it would inherit
`CGO_LDFLAGS="-shared …"` from the Dockerfile and link as a shared object, not as an executable
(§5).

`plugin/authz/authz.go`, sketched as a contract (the engineer writes the body):

```go
package authz

// PublishScope is a fifth spelling of Scope.Sse.Events.Publish (Go cannot import a C# const);
// AS-4's architecture guard and the realm JSON hold the C# side.
const PublishScope = "sse.events.publish"

// HasScope: claim is the raw `scope` value from jwt.MapClaims. True only for a string whose
// space-split (empty entries removed), ordinal tokens include scope. Mirrors
// RequireScopeExtensions.AddScopePolicies. Any non-string (nil, []any, number) is false.
func HasScope(claim any, scope string) bool

type Verdict int
const (
    Defer Verdict = iota // let the next ACL check (acl.txt) decide
    Deny
)

// Grants remembers, per MQTT username, whether that identity's most recent successful JWT
// authentication carried PublishScope. Safe for concurrent use (sync.RWMutex).
type Grants struct { /* mu sync.RWMutex; byUsername map[string]bool */ }
func NewGrants() *Grants
func (g *Grants) Record(username string, canPublish bool)
// Decide: !isWrite → Defer; username unknown → Defer; recorded true → Defer; recorded false → Deny.
func (g *Grants) Decide(username string, isWrite bool) Verdict
```

`Decide` never returns "allow". That is deliberate (spec finding 2): the strongest thing the
plugin can say is "not refused by me".

### 2.2 `jwt_auth.go`

- `var grants = authz.NewGrants()` at package level, next to the JWKS state.
- In `sseOnBasicAuth`, after the issuer check passes and before `return C.MOSQ_ERR_SUCCESS`:
  ```go
  canPublish := authz.HasScope(claims["scope"], authz.PublishScope)
  grants.Record(username, canPublish)
  if !canPublish { logf("%s authenticated without %s; its publishes will be refused", username, authz.PublishScope) }
  ```
  Only a **successful** JWT authentication records anything. A rejected token or a non-JWT
  password (the `PLUGIN_DEFER` path) leaves the memory untouched. The log line is per CONNECT
  (NFR-002 path, one stderr write) and never per PUBLISH. It is what tells an operator that a
  `Denied PUBLISH` came from the scope rather than from `acl.txt`.
- New export:
  ```go
  //export sseOnAclCheck
  func sseOnAclCheck(event C.int, eventData unsafe.Pointer, userData unsafe.Pointer) (result C.int)
  ```
  - `defer recover` → `C.MOSQ_ERR_ACL_DENIED`. This is fail-closed, the same pattern as basic auth.
  - `ev := (*C.struct_mosquitto_evt_acl_check)(eventData)`.
  - `name := C.mosquitto_client_username(ev.client)`. If it is `nil`, return
    `C.MOSQ_ERR_PLUGIN_DEFER`.
  - `grants.Decide(C.GoString(name), ev.access == C.MOSQ_ACL_WRITE)`: `Deny` →
    `C.MOSQ_ERR_ACL_DENIED`, `Defer` → `C.MOSQ_ERR_PLUGIN_DEFER`. **Never `MOSQ_ERR_SUCCESS`.**
- Header comment: extend the "What a JWT-shaped password must satisfy" paragraph with the
  publish rule and the defer-only semantics, in one short paragraph. The `iss` sentence stays
  accurate as written.

### 2.3 `mosquitto_glue.c`

`sse_register` registers both callbacks and returns the first non-success:

```c
int rc = mosquitto_callback_register(id, MOSQ_EVT_BASIC_AUTH, (MOSQ_FUNC_generic_callback)sseOnBasicAuth, NULL, NULL);
if(rc != MOSQ_ERR_SUCCESS) return rc;
return mosquitto_callback_register(id, MOSQ_EVT_ACL_CHECK, (MOSQ_FUNC_generic_callback)sseOnAclCheck, NULL, NULL);
```

`goPluginInit` currently ignores `sse_register`'s result (`jwt_auth.go:90`). It now returns that
result, so a failed registration fails broker start instead of silently running without
enforcement. **No `MOSQ_EVT_DISCONNECT` registration** (§3 explains why).

## 3. Why the verdict is keyed by username, not by connection

The obvious design is a map keyed by the `struct mosquitto *` context, deleted on
`MOSQ_EVT_DISCONNECT`. **It fails open for Wills.** `context__disconnect` fires the disconnect
event and only then sends the Will, through an `MOSQ_ACL_WRITE` check (spec finding 3). By then
the entry is gone, the plugin defers, and `acl.txt` alone lets the Will through. Not deleting
would leak one entry per context address, which is unbounded.

Keying by the MQTT **username** has neither problem:

- `azp == username` is already enforced at CONNECT. So the username is the Keycloak client, and
  the scope is a *default* client scope of that client (spec A2), the same in every token it
  mints.
- The Will (and a delayed Will) is checked against a context whose username is still set
  (`context__cleanup` clears it only after the Will has been handled or discarded).
- Memory is bounded by distinct identities, not by connections (spec A3).
- **Consequence, stated:** the most recent successful authentication for a username sets the
  verdict for all of that username's live sessions. If an admin removes the scope, the next
  connect revokes publishing for the existing sessions too, which is stricter and acceptable. If
  an admin adds it, older sessions gain it. That is the same principal, and it is now granted.

## 4. The realm and the ACL

- `src/AppHost/Realms/smart-sentinel-eye-realm.json`, `scenario-simulator.defaultClientScopes`:
  append `"sse.events.publish"`. Touch nothing else. Keep the encoding and the UTF-8 BOM. The
  `description` is unchanged, so the 255-character limit (project memory) is not at risk.
- `src/AppHost/mosquitto/acl.txt`: append a probe block with a comment saying why it exists:
  ```
  # Spec 330 (#2286). A fixed identity for MqttPublishScopeIntegrationTests: the
  # only way to show a publish refused *for the scope* is a user acl.txt would
  # otherwise allow. No realm client has this id; the test creates and deletes it.
  # The topic is outside fab/+/+/+, so nothing ingests what the probe publishes.
  user mqtt-publish-scope-probe
  topic write sse-probe/publish-scope
  ```
  It is safe in the shared dev ACL: the grant names one topic no service subscribes to, and only
  a realm admin can create a client with that id.

## 5. Tests

| ID | Where | Kind | Before → after |
|---|---|---|---|
| T-A | `tests/Integration.Tests/Identity/MqttPublishScopeIntegrationTests.cs` (new) | Aspire fixture, AS-1..3 | AS-1 **red** → green; AS-2, AS-3 green → green |
| T-B | `tests/Architecture.Tests/MqttPublishGrantTests.cs` (new) | file-level, AS-4 | **red** → green |
| T-C | `src/AppHost/mosquitto/plugin/authz/authz_test.go` (new) | Go unit, AS-5/AS-6, run in the image build | written after `authz` exists; teeth proven by counterfactual |

**T-A** mirrors `MqttAudienceIntegrationTests`' shape. That includes its premise-before-conclusion
rule: decode the token and assert `aud`, `azp` and the presence or absence of the scope before
any MQTT call. It also asserts that the CONNACK is distinguished from a missing CONNACK.
Differences from that class:
- **MQTT 5** (`MqttProtocolVersion.V500`). Only v5 carries a PUBACK reason code (spec finding 5).
- The probe client id is **fixed** (`mqtt-publish-scope-probe`, matching the ACL row). Each fact
  deletes any leftover client of that id first (by `clientId` lookup), then creates its own.
  `DisposeAsync` deletes it. The facts in one class run sequentially, so the fixed id cannot
  collide within a run.
- AS-1 creates the client with `defaultClientScopes = [sse-audience]`, and AS-2 with
  `[sse-audience, sse.events.publish]`. AS-3 registers a device through
  `POST /devices/register?fabId=munich`, as `MqttAudienceIntegrationTests.RegisterDeviceAsync`
  does.
- Publish at QoS 1 and assert `MqttClientPublishResult.ReasonCode`.
- The private helpers (create client, mint, connect) are copied, not extracted. A shared helper
  would refactor `MqttAudienceIntegrationTests` inside a behaviour-changing PR (ADR-0036).
- **Needs a `ci-shards/shard-N.filter` entry** (project memory). Add it to the shortest shard.

**T-B** parses `acl.txt` into `user` blocks (a `user X` line starts a block, a `topic write …`
or `topic readwrite …` line inside it is a write grant, and `#` lines are comments). For every
user with a write grant that is also a `clientId` in the realm's `clients[]`, it asserts that
`defaultClientScopes` contains `Scope.Sse.Events.Publish`. Paths are built and normalised the way
`ScopeGrantTests` / `RealmIdentityTests` locate the realm file (project memory: normalise slashes
in source-scanning tests). Population gate: the fact must find **at least one** such user, or it
fails as vacuous. Today `scenario-simulator` is the one, and it fails the assertion, which is the
red.

**T-C** is table-driven, with `t.Run` sentence-style subtest names (ADR-0053's intent, in Go
form). It covers every AS-5 row and every AS-6 row, plus the transitions in both directions. It
uses no `-race`, because that needs cgo. It runs through a new step in Dockerfile stage 2, placed
after `COPY plugin/ ./` and before `go build`:

```dockerfile
RUN CGO_ENABLED=0 go test ./authz/...
```

`CGO_ENABLED=0` is load-bearing: the stage's `CGO_LDFLAGS=-shared …` would otherwise turn the
test binary into a shared object. Putting the step in the build means **every image build runs
the tests**, including each Aspire boot and each CI integration/e2e job. A failing test fails the
`mosquitto` resource loudly, and no workflow change is needed. It adds no `FROM`, so
`DockerfileUpstreamPinTests` is unaffected. To run it alone:
`docker build --target plugin_builder src/AppHost/mosquitto`.

**Red-first, declared.** ADR-0139's observed red for this behaviour is T-A AS-1 and T-B, run
against the unchanged plugin and realm. T-C targets a package that does not exist before 4b.
Running it first would show a compile error, which is not a red. So T-C is written after `authz`
lands, and each subtest's teeth are proven by **counterfactual**: invert `Decide`'s deny branch,
make `HasScope` substring-match, and swap `Record`'s overwrite for first-wins. Each mutation must
turn named subtests red, the output is quoted, and the mutation is reverted.

## 6. Latency and NFRs

- **Event ingress** (feeds §IV *Event → overlay state*): per PUBLISH, one cgo call,
  `C.GoString(username)`, an `RLock` and a map read. That is sub-microsecond to low-microsecond,
  against spec 006's 50 ms ingest budget. Measured by `IngestThroughputMeasurementTests`, run
  twice on develop and twice on the branch.
- **NFR-002 (connect ≤ 5 ms p99):** adds one string split and one map write per JWT CONNECT.
  Measured by `NFR002_MqttConnectAuthTests`, run twice.
- No other §IV leg is touched. No dashboard obligation (§VII) attaches, because no leg is built
  or changed in kind.

## 7. Commits, follow-up, gate

| # | Commit | Author | Builds alone | Suite state |
|---|---|---|---|---|
| C1 | `chore(mosquitto): give a probe identity one write topic for the publish-scope test` (acl.txt only) | infra-engineer | yes | green (no existing identity changes) |
| C2 | `test(mosquitto): prove the broker lets a token without sse.events.publish publish` (T-A + shard entry + T-B) | test-writer | yes | **red: AS-1, AS-4** |
| C3 | `fix(realm): grant scenario-simulator the publish scope it already exercises` | infra-engineer | yes | AS-4 green; AS-1 still red |
| C4 | `fix(mosquitto): refuse a publish whose token lacks sse.events.publish` (`authz/`, `jwt_auth.go`, glue, Dockerfile step, comment sweep) | infra-engineer | yes | green |
| C5 | `test(mosquitto): pin scope parsing and the publish verdict table` (T-C) | test-writer | yes | green; counterfactuals quoted |

C3 lands before C4 so that no commit enforces the scope while the simulator lacks it. A commit
with enforcement but without the grant would turn about twelve ingest classes red for a reason
unrelated to any bisect.

**Comment sweep in C4:** the `mosquitto.conf` header ("It requires: …") gains the publish rule;
`ScopeGrantTests.cs:66`'s "no static client holds it" becomes "`scenario-simulator` holds it
statically; devices get it from `KeycloakScopeBundles.Device`"; and `Scope.cs`'s `Publish` doc
becomes "Granted to MQTT publishers (devices, and the dev-only scenario-simulator); not to
humans." No other files change.

**Follow-up issue (filed at phase 7 by the orchestrator, `tech-debt`, Project #13, no
`agent:ready`):** *"The Mosquitto plugin's issuer check is a suffix match"*. Body: the
`jwt_auth.go` suffix test, why an exact check needs the clients' issuer rather than the JWKS
host (spec §Out of scope 1: no `KC_HOSTNAME`, request-derived `iss`, broker on the container
network), and the options with their costs: inject the expected issuer from AppHost, or set
`KC_HOSTNAME`. Both need a human choice. Refs #2286.

**Gate:** `Closes #2286`. The decided half (enforce) is fully delivered. The issuer nit is split
out with its reason, not dropped.
