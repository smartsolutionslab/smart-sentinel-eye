# Feature Specification: The hook answers the action it was asked

**Branch**: `fix/2094-the-hook-answers-the-action-it-was-asked` · **Issue**: #2094
**Created**: 2026-09-05 · **Status**: Phase 1 complete, awaiting review
**ADRs**: 0011, 0036, 0038, 0046, 0047, 0051, 0053, 0070, 0084, 0089, 0103,
0105, 0109, 0139, 0141, 0144 — **no new ADR**
**Specs it continues**: 002 (the SFU and the auth hook), 041 (the kiosk may
watch), 071 (`aud` on this same hook), 073 (a catalogued scope that nothing
enforces)

---

## Summary

MediaMTX's external-auth hook posts an **`action`** field naming the operation
being attempted — `read`, `publish` or `playback`. `POST /streams/authorize`
never reads it. `mediamtx.yml` excludes only `api`, `metrics` and `pprof` from
the hook, so `read`, `publish` and `playback` all arrive here and all receive
the same answer: **whatever `sse.streams.read` earns.**

This spec makes the hook answer the action it was asked. `read` and `playback`
keep today's answer. `publish` is refused, with the reason stated at the site.
An action that is absent or not one of the three is refused too.

---

## Everything in the issue was checked against the tree

All four claims hold. One is stated more precisely than the issue could, and one
correction is worth recording.

| Claim | Verdict | Evidence |
|---|---|---|
| MediaMTX posts an `action` field of `read`/`publish`/`playback` | **True** | Upstream `docs/2-features/06-authentication.md`, quoted below. The full value set is `publish\|read\|playback\|api\|metrics\|pprof`. |
| The handler never reads it | **True** | `MediaMtxAuthorizeRequest(string? Token, string? Path)` — `src/StreamDistribution/Api/StreamEndpoints.cs:402`. The record has two members; `action` is discarded by the JSON binder. |
| `mediamtx.yml` excludes only `api`, `metrics`, `pprof` | **True** | `src/AppHost/Resources/mediamtx.yml:46-49`. |
| Every path is created with a static `source`, so MediaMTX refuses publishers | **True** | `MediaMtxRtspGateway.cs:32` posts `new { source = rtspSourceUrl }`; `RepointPathAsync` (`:48`) patches the same field. Upstream `internal/core/path.go`'s `doAddPublisher` refuses with `"can't publish to path '%s' since 'source' is not 'publisher'"`. |
| `paths: {}` defines no catch-all | **True** | `mediamtx.yml:53`. Upstream reserves `all_others` for that, and it is absent. |

**The correction.** The issue cites the record at `StreamEndpoints.cs:402`. That
is the file's **last** line and the record's declaration line — accurate, but
the file is 402 lines long and already over ADR-0084's 300-LOC ceiling, which
matters to how this change is shaped (see *Risks* in `plan.md`).

### The upstream payload, verbatim

From `bluenviron/mediamtx`, `docs/2-features/06-authentication.md`:

```json
{
  "user": "user",
  "password": "password",
  "token": "token",
  "ip": "ip",
  "action": "publish|read|playback|api|metrics|pprof",
  "path": "path",
  "protocol": "rtsp|rtmp|hls|webrtc|srt",
  "id": "id",
  "query": "query",
  "userAgent": "userAgent"
}
```

> If the URL returns a status code that begins with `20` (i.e. `200`),
> authentication is successful, otherwise it fails.

**There is no pinned version to check this against.** `AppHost.cs:134` runs
`bluenviron/mediamtx:latest-ffmpeg` — a floating tag, on all three MediaMTX
containers. The field name and its values were verified against upstream `main`
today. Pinning the image is a real and separate problem; it is named in *Out of
scope* rather than folded in.

---

## Is the gap real?

**Yes, structurally — and it is inert today for a reason that is not ours.**

The handler's answer is derived from the token alone
(`AuthorizeWhepCommandHandler.cs:39-64`): a token, the `sse.streams.read` scope
or the grandfathered `sse.management` bundle, and a stream that is not Offline.
Nothing in that chain distinguishes a viewer from a publisher, so a bearer that
can watch a wall gets a `200` for `action: "publish"` on the same path.

What stops the publish is MediaMTX, one layer later, because our paths carry a
static `source` and `doAddPublisher` refuses. **That is a property of
`mediamtx.yml` and `MediaMtxRtspGateway`, not of this endpoint.** Three edits
that a reasonable person could make without touching auth would remove it:

1. a `paths:` entry, or an `all_others` entry, with no `source` — the shape
   every "let a camera push to us" feature needs;
2. a path added through the control API with `source: publisher`, which
   `MediaMtxRtspGateway` does not do today but is one field away from doing;
3. `overridePublisher` semantics changing upstream under the floating tag.

The outcome behind that door is not a read gap. It is **an authenticated
viewer's token replacing the picture on an industrial CCTV wall**, on a system
whose operators make decisions from what those walls show. So the defence
belongs in the handler, which is what #2094 asks for and what this spec does.

**Stated honestly, as spec 071 stated its own narrowness**: nobody can exploit
this today, and this change closes no open path. It moves a safety from another
component's configuration file into the code that is supposed to own it, and
adds the test that fails if someone reopens the door.

---

## Does anything publish through the hook? (the outage question)

**No. Nothing in this product publishes through `POST /streams/authorize`, and
four independent lines of evidence say so.**

1. **No WHIP anywhere.** `grep -rn "whip\|WHIP"` across `apps/`, `src/` and
   `tests/` (excluding `node_modules`, `obj`, `bin`) returns **zero** hits.
   `WhepClient.ts` does exactly one `POST` — to `opts.whepUrl`
   (`apps/shared/src/streaming/WhepClient.ts:119`) — and one `DELETE` to the
   session resource (`:198`). The browser never publishes.

2. **Both publishers publish to a different server.** Every FFmpeg publish
   command in the repo targets `rtsp://localhost:8554/$MTX_PATH` —
   `CameraSimProvisioner.cs:25` and `:62`, and `fixture-video.yml:51-52`. That
   `localhost` is **inside the `camera-sim` / `fixture-video` container**, not
   the main SFU. Those two servers do not use the hook at all: neither config
   sets `authMethod: http`, and both grant publish through
   `authInternalUsers` (`camera-sim.yml:30-36`, `fixture-video.yml:36-41`).
   The main MediaMTX is a *reader* of both, pulling over RTSP
   (`SimulatorOptions.RtspHost` = `camera-sim:8554`), and that pull is
   authenticated by *their* internal users, not by ours.

3. **Path provisioning cannot be affected.** `MediaMtxRtspGateway`'s
   `add`/`patch`/`delete`/`list`/`get` calls are `action: api`, which
   `mediamtx.yml:47` **excludes** from the hook. Refusing an unrecognised action
   therefore cannot break provisioning, reconciliation or the health sweep.

4. **The empirical proof, and the strongest one.** The hook answers **401 for
   an empty token** (`AuthorizeWhepCommandHandler.cs:39-42`), and MediaMTX
   composes the hook body itself — an internal static-source pull carries no
   bearer. If any internal MediaMTX operation were routed through this hook it
   would already be failing today, and video demonstrably works: spec 056's
   fixture exists to put a picture on the wall, spec 045 measured a wall, and
   `WhepHandshakeLatencyTests` / `SfuLatencyIsReadableTests` run against it.
   **Nothing internal uses this hook.**

**So refusing `publish` outright breaks nothing.** The issue's own suggested
answer survives verification.

### Why `publish` is refused rather than gated on a scope

`sse.streams.write` exists in the catalogue (`Scope.cs:31`) and in the realm
(`smart-sentinel-eye-realm.json:45`), and is **granted by default to
`management-web`** (`:173`). Mapping `publish` to it would hand a
video-injection primitive to every signed-in operator's browser token — the
opposite of the fix. `sse.streams.write` means *mutate stream resources*
(provision, repoint, retire), not *inject video*, and no endpoint enforces it
today (the shape spec 073 was written about). **It is not the answer here**, and
inventing a fourth scope for a publisher that does not exist is the speculative
generality ADR-0036 rules out. Refusal is both the smallest change and the
correct one.

---

## Decisions

### D1 — `read` and `playback` are both admitted; `publish` is refused

`read` is the WHEP open the product depends on. `playback` is MediaMTX's
recording-playback action; the product does not enable recording, so no
`playback` reaches the hook today — but admitting it costs nothing and refusing
it would make enabling recording later look like an auth bug. Both are reads of
video by a viewer who holds `sse.streams.read`.

`publish` is refused for every caller, with no scope that grants it, because
nothing in this product publishes through this hook (evidence above).

### D2 — an absent or unrecognised action is **refused** (fail closed)

**This is the judgement call in the spec, so the reasoning is written out.**

Two sub-cases, deliberately given the same answer and different error codes:

- **Unrecognised** (`api`, `metrics`, `pprof`, or a value upstream adds).
  Refusing is plainly right: those three reach the hook only if someone deletes
  an `authHTTPExclude` line, and a read scope must not then buy API access. A
  new upstream action must earn its answer by being read, not inherit one.
- **Absent or empty.** This is the version-skew case, and it is the one that
  cuts both ways: fail closed and the hook dies the day MediaMTX stops sending
  the field; fail open and today's gap silently returns the same day.

**Fail closed, for four reasons.**

1. **The asymmetry the issue names.** A loud outage — no video, one message —
   is recoverable in minutes. A silently reopened injection path on a CCTV wall
   is not, and nobody would know to look.
2. **This repository's specific failure mode.** CLAUDE.md records four rules
   that drifted because nothing checked them: §II twice, §IV's leg table, the
   Phase-3 board gate. A safety that quietly stops applying is *exactly* that
   defect. Failing closed cannot drift silently.
3. **Nothing an attacker controls.** MediaMTX composes the hook body; a WHEP
   client controls only the path and the token. A caller who reaches
   `/streams/authorize` directly and omits `action` gains nothing — MediaMTX is
   not in that conversation, so a `200` there authorizes nothing. Failing open
   on absence is therefore not a live vulnerability, only a regression risk;
   failing closed is not a live vulnerability either, only an availability risk.
   Between two risks of the same class, (1) and (2) decide it.
4. **The break is made diagnosable rather than mysterious.** A distinct error
   code (`WHEP_ACTION_UNKNOWN`) and a warning naming MediaMTX version skew turn
   "video died" into "video died, and here is the field that went missing".
   That is the whole mitigation for reason 1's cost, and FR-011 requires it.

**Recorded as an assumption, because it is one:** the field is documented,
stable, and the only way any MediaMTX auth server can tell a publisher from a
viewer — removing it would break every such integration in existence. If it is
ever removed, this decision is the thing to revisit.

### D3 — refuse with **403**, never 401

Upstream, verbatim:

> This happens because RTSP clients don't provide credentials until they are
> asked to. In order to receive the credentials, the authentication server must
> reply with status code `401`, then the client will send credentials.

**A `401` is a credential challenge that invites a retry.** MediaMTX treats any
non-`2xx` as a failure, so both codes deny — but `401` specifically tells the
client to come back with credentials, which is the retry the issue asked about.
A refusal on the *action* has nothing to do with the caller's credentials and
must be terminal, so it is `403`. This matches the existing shape: `Forbidden`
and `StreamUnavailable` are already `403`, `Unauthorized` alone is `401`
(`AuthorizeWhepErrors.cs:13-29`).

### D4 — the action is checked **first**, before the token

The action decision is independent of who is calling. Checking it first means a
`publish` is refused identically with a valid token, an invalid token or none —
and, per D3, is never answered `401`, so it never triggers the credential
challenge. It is also the cheapest check on the WHEP handshake.

### D5 — the wire DTO carries the raw string; the decision lives in the handler

`MediaMtxAuthorizeRequest` gains `string? Action`. It is an Api-layer wire DTO
translating MediaMTX's JSON, which is where a nullable primitive belongs —
the same exemption ADR-0141 gives Api and Infrastructure, and the same shape
`Token` and `Path` already have.

The **decision** goes in `AuthorizeWhepCommandHandler`, because that is where
every other part of this answer already lives. The action reaches it as a value
object on `AuthorizeWhepCommand`, not as a string (D6).

### D6 — `MediaMtxAction` is a value object beside `MediaMtxPath`

A closed set of three MediaMTX protocol tokens, mirroring `TranscodeMode`
(`Domain/Stream/TranscodeMode.cs`) — a `sealed record : IValueObject<string>`
with static instances and a throwing `From`. It sits in
`StreamDistribution/Domain/Stream/` beside `MediaMtxPath`, which is the same
kind of thing: a MediaMTX-protocol string this context owns (ADR-0038, ADR-0046,
ADR-0092).

It also gets `TryFrom(string?) -> Option<MediaMtxAction>`, and **that `Option` is
the fail-closed decision made explicit** (ADR-0141): absent, empty and
unrecognised all become `None`, and the handler refuses `None`. `MediaMtxPath`
already has the non-throwing probe `IsCanonical` for the same reason.

The command's parameter is `Option<MediaMtxAction> Action` with **no default
value**. `Option<T>` is a `readonly struct` whose `None` is `default`
(`Shared.Kernel/Option.cs:29`), so a defaulted parameter would silently mean
"unknown" at every call site that forgot it. The compile error is the feature.

### D7 — rejected: reading `protocol`, `ip`, `query`, `id` or `userAgent`

MediaMTX sends seven more fields. None is needed to close this gap, and each
would be a decision about what to do with it. `action` alone, for this issue
alone.

### D8 — rejected: changing `mediamtx.yml`

Tempting edits — a `paths:` guard, a comment, another exclusion — all move the
safety back into the config file this spec is removing it from. The config is
correct as it stands. Not touched.

---

## User Scenarios & Testing

### User Story 1 — a viewer's token cannot publish (P1)

The only story, and the whole slice. MediaMTX posts an action with every hook
call; the hook answers on it.

**Independently shippable**: three source files changed, one new value object,
one new test class plus additions to two existing ones. No realm change, no
frontend change, no migration, no Aspire resource, no `mediamtx.yml` change.

#### Acceptance scenarios

```gherkin
Scenario: the happy path is unchanged
  Given a kiosk token carrying sse.streams.read
  When MediaMTX posts it to POST /streams/authorize with action "read" for a live path
  Then the response is 200
```

```gherkin
Scenario: playback is a read too
  Given a kiosk token carrying sse.streams.read
  When MediaMTX posts it to POST /streams/authorize with action "playback"
  Then the response is 200
```

```gherkin
Scenario: a read scope does not authorize a publish
  Given a token carrying sse.streams.read for a path the caller may watch
  When MediaMTX posts it to POST /streams/authorize with action "publish"
  Then the response is 403
  And the problem code is WHEP_ACTION_NOT_PERMITTED
  And it is not 401 — nothing about the caller's credentials would change the answer
```

```gherkin
Scenario: the grandfathered bundle does not authorize a publish either
  Given a token carrying the sse.management bundle
  When MediaMTX posts it with action "publish"
  Then the response is 403
  And the problem code is WHEP_ACTION_NOT_PERMITTED
```

```gherkin
Scenario: a publish is refused before the token is even looked at
  Given a body whose token is null, empty or "this-is-not-a-jwt"
  When MediaMTX posts it with action "publish"
  Then the response is 403 and not 401
  And the problem code is WHEP_ACTION_NOT_PERMITTED
```

```gherkin
Scenario: an action MediaMTX did not send is refused
  Given a valid kiosk token
  When the hook receives action "api", "metrics", "pprof" or "sideload"
  Then the response is 403
  And the problem code is WHEP_ACTION_UNKNOWN
```

```gherkin
Scenario: a missing action is refused, and says why
  Given a valid kiosk token
  When the hook receives a body with no action field, or an empty one
  Then the response is 403
  And the problem code is WHEP_ACTION_UNKNOWN
  And a warning is logged naming MediaMTX version skew as the cause to check
```

```gherkin
Scenario: an unparseable path is still answered on the path
  Given any token and action "read"
  When the path is "not-a-cam-guid"
  Then the response is 403 with WHEP_INVALID_PATH
  And the existing behaviour is unchanged
```

**No conflict scenario.** The endpoint neither writes nor versions anything;
`If-Match` and 409 (ADR-0113) do not apply. Recorded rather than omitted.

**No bad-request scenario.** Every refusal on this endpoint is 401 or 403 by
design — MediaMTX reads only "begins with 20 or not", and a 400 would be a
status the caller cannot act on. An unparseable path is already 403; an
unparseable action joins it.

**No auth-scope scenario beyond the above.** The route is `AllowAnonymous` at
routing and hand-validates in the handler (spec 002 FR-007); that shape is not
touched.

---

## Requirements

- **FR-001** `MediaMtxAuthorizeRequest` gains `string? Action`, a third
  positional member on the Api-layer wire DTO. Its XML doc names it as one of
  the fields MediaMTX sends, and the doc's list of "accepted but ignored"
  fields is corrected to no longer include `action`.
- **FR-002** A new value object `MediaMtxAction` in
  `src/StreamDistribution/Domain/Stream/`, mirroring `TranscodeMode`: a
  `sealed record : IValueObject<string>` with statics `Read`, `Publish`,
  `Playback`, a throwing `From(string)`, and `ToString()` returning the value.
- **FR-003** `MediaMtxAction.TryFrom(string?)` returns
  `Option<MediaMtxAction>` — `None` for null, empty, whitespace, and any value
  outside the three. Matching is **ordinal and case-sensitive**: MediaMTX sends
  these three lowercase literals, and a case-insensitive match would accept a
  spelling MediaMTX never sends.
- **FR-004** `AuthorizeWhepCommand` gains a third member
  `Option<MediaMtxAction> Action`, **with no default value**.
- **FR-005** `AuthorizeWhepError` gains two `403` variants —
  `ActionNotPermitted` (`WHEP_ACTION_NOT_PERMITTED`) and `ActionUnknown`
  (`WHEP_ACTION_UNKNOWN`) — with matching factories on
  `AuthorizeWhepFailures` (ADR-0047, ADR-0089).
- **FR-006** `AuthorizeWhepCommandHandler` decides the action **first**, before
  the token is inspected (D4): `None` → `ActionUnknown`; `Publish` →
  `ActionNotPermitted`; `Read` or `Playback` → fall through to the existing
  token, scope and stream-state checks, unchanged.
- **FR-007** The handler reads three fields, so it deconstructs the command
  into locals as its first statement after the guard (CLAUDE.md house rule); it
  reads two today and already does.
- **FR-008** `AuthorizeWhep` in `StreamEndpoints.cs` parses
  `MediaMtxAction.TryFrom(body.Action)` alongside the existing path parse and
  passes the `Option` to the command. **The endpoint does not decide** — it
  translates.
- **FR-009** The endpoint declares nothing new in OpenAPI beyond what it has:
  `403` is already declared. `WithSummary` gains one sentence saying the hook
  answers on the action, that `read` and `playback` are admitted, and that
  `publish` is refused outright because nothing in this product publishes
  through the hook.
- **FR-010** The refusal states its reason **at the site**: a comment on the
  `Publish` branch of the handler recording that nothing in this product
  publishes through this hook, and that the previous safety was MediaMTX's
  static `source` rather than this code. That is the issue's third bullet.
- **FR-011** The `ActionUnknown` path logs a **warning** through
  `[LoggerMessage]` source-gen on the context's existing `Log` class
  (`Application/Log.cs`, ADR-0050), naming the received value and MediaMTX
  version skew as the thing to check. `ActionNotPermitted` logs a warning too,
  naming the path and the action — a refused publish is a security-relevant
  event and must not be silent.
- **FR-012** `sse.streams.write` is **not** referenced by this change (D1's
  second half). A diff mentioning it means the plan was misread.
- **FR-013** No realm file change, no `mediamtx.yml` change, no AppHost change,
  no new package reference, no new project reference, no frontend change.
- **FR-014** **No assertion in any existing test is changed.** Two categories of
  existing test are edited, both by correcting an *input* to match what MediaMTX
  actually sends, never an expectation:
  - `AuthorizeWhepCommandHandlerTests` (6 constructions) gains
    `Option<MediaMtxAction>.Some(MediaMtxAction.Read)` — a compile-driven edit
    from FR-004.
  - `WhepAuthIntegrationTests` (5 bodies) gains `action = "read"`. These
    fixtures stand in for MediaMTX and today post a body MediaMTX never sends;
    adding the field makes the fake faithful. **Every asserted status stays
    identical.** If any of them changes status, the plan is wrong — block and
    report (ADR-0139, and the lane's rule against editing a test to pass).

## Success criteria

- **SC-001** A token carrying `sse.streams.read` is refused `403` for
  `action: "publish"`, provable without Docker.
- **SC-002** The same token is still answered `200` for `action: "read"` and
  `action: "playback"` — the fix is not bought by refusing everyone.
- **SC-003** An absent or unrecognised action is refused `403`
  `WHEP_ACTION_UNKNOWN`, and the refusal is logged with a diagnosable reason.
- **SC-004** A publish is never answered `401`, so MediaMTX never turns the
  refusal into a credential challenge (D3).
- **SC-005** The full suite passes with no assertion edited (FR-014).
- **SC-006** At phase 5, a real MediaMTX hook call is observed carrying
  `action: "read"`, and a hand-posted `action: "publish"` with a real viewer
  token is observed being refused `403`. Statuses and the hook body recorded.

---

## Independent end-to-end test procedure

**Steps 3-6 need Docker, which is unresponsive on this machine and needs a
manual restart.** Steps 1-2 are the whole of the automated evidence and need
nothing.

1. `dotnet test tests/StreamDistribution.Application.Tests` — the handler-level
   red, produced entirely with `FakeWhepAuthValidator` and
   `InMemoryStreamRepository`. No stack, no signing key, no minted token, no
   container.
2. `dotnet test tests/StreamDistribution.Domain.Tests` — `MediaMtxAction`'s own
   parse table, including the fail-closed `None` cases.
3. `dotnet run --project src/AppHost`, wait for `mediamtx` and
   `stream-distribution` healthy.
4. **Observe the real hook body.** Open the kiosk at `http://localhost:5174`,
   sign in, open a wall. In the Aspire dashboard, read
   `stream-distribution`'s structured logs for the `/streams/authorize` request
   and record the `action` value MediaMTX actually sent. *This is the step that
   converts D2's documented assumption into an observed fact for the version
   running here* — do not skip it. Video plays: the happy path observed rather
   than asserted.
5. **The negative, by hand.** Mint through the **Aspire proxied endpoint** (not
   the container's mapped port, or the issuer will not match and everything
   401s for the wrong reason):
   `POST {keycloak}/realms/smart-sentinel-eye/protocol/openid-connect/token`
   with
   `grant_type=password&client_id=smart-sentinel-eye-web&username=admin&password=Admin1234&scope=openid sse.management`.
   Then
   `POST {stream-distribution}/streams/authorize` with
   `{"token":"<that token>","path":"cam-<guid>","action":"publish"}` → **403**,
   body code `WHEP_ACTION_NOT_PERMITTED`. Repeat with `"action":"read"` →
   **200**. Repeat with the `action` key omitted → **403**,
   `WHEP_ACTION_UNKNOWN`, and confirm the warning appears in the dashboard.
6. Confirm the wall from step 4 is **still playing** after step 5 — the refusals
   are per-request and did not disturb an open session.

Step 5 is a phase-5 drill rather than an integration test for the reason spec
071 gave about its own drill: making MediaMTX itself attempt a publish would
require adding a source-less path to `mediamtx.yml`, which is the exact
configuration D8 declines to make and which a test must not create under itself.
The refusal is asserted at the handler; the *wire* is observed here.

---

## Locked tech choices

MediaMTX as the SFU with an HTTP external-auth hook (ADR-0011, spec 002 FR-007);
minimal APIs (ADR-0070); hand-rolled command handlers (ADR-0042); maximalist
hand-written value objects with `IValueObject<T>` and `.From(...)` (ADR-0038,
ADR-0046); per-aggregate Domain folders (ADR-0092); `Result<T, Error>` over
`ApiError(Code, Message, HttpStatusCode)` (ADR-0047, ADR-0089); `Option<T>` for
absence in Domain and Application (ADR-0141); `Ensure.That` guards (ADR-0105);
`[LoggerMessage]` source-gen logging (ADR-0050); xUnit + Shouldly, hand-written
fakes, no Testcontainers (ADR-0052, ADR-0054, ADR-0103); sentence-style test
names (ADR-0053).

## Latency budget impact

**N/A to all six legs of constitution §IV.** The check is one ordinal string
comparison against three literals, on the WHEP **open**, which is not on the
`event → overlay` path at all — that path runs over an already-established hub
connection and an already-open media session. `WhepHandshakeLatencyTests` covers
the handshake's cost and is unaffected by a comparison against three constants.
No leg's dashboard obligation (§VII, ADR-0117) is created or discharged.

## Out of scope

- **#2092 — the missing fab check on this same handler.** A caller holding
  `sse.streams.read` can open any fab's stream. Different gap, needs an ADR, and
  is held for a human. **A diff from this spec that touches
  `IFabAuthorizationGuard`, `FabResolution` or `FabIdentifier` has drifted.**
- **Pinning `bluenviron/mediamtx`.** All three containers run the floating
  `latest-ffmpeg` tag (`AppHost.cs:134`, `:166`, `:552`). That is a real
  supply-chain and skew problem, it is what makes D2 a judgement rather than a
  lookup, and it is an infra change to `AppHost.cs` with its own blast radius.
  Not folded in; worth its own issue.
- Reading MediaMTX's other seven payload fields (D7).
- Any change to `mediamtx.yml` (D8).
- A scope for publishing, or enforcing `sse.streams.write` anywhere (D1).
- Moving the token out of the JSON body into a header — that is MediaMTX's
  protocol and would be an architectural decision (spec 071's out-of-scope note
  still stands).
