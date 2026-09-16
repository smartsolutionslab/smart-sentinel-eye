# Spec 168 — A gateway that says what it waits for

**Issue:** #2407
**Branch:** `chore/2407-a-gateway-that-says-what-it-waits-for`
**Decisions of record:** **ADR-0106** (single YARP gateway at the edge; realtime
stays direct), **ADR-0153** (one instance per service, until a service earns
otherwise — `Accepted`, PR #2415, not yet merged to `develop` at time of
writing; cited as evidence, not depended on). Secondary: **ADR-0037** (phases),
**ADR-0144** (autonomous lane), **ADR-0086** (no `Co-Authored-By`).
**Predecessor:** `specs/165-wiring-that-matches-its-consumer/` §7 — which
noticed this exact absence while establishing its own baseline and filed it
as this issue rather than fixing it in scope: *"Same shape, larger blast
radius, and no evidence anything needs it."*
**Constitution:** §IV — checked and **N/A**; see §6.

---

## 1. Why this needs a spec at all, when the diff may be one comment

**The likely outcome is a single comment block, no code-behaviour change.**
ADR-0037 permits "no spec — one line" for a trivial change, and on line count
this qualifies more strongly than spec 165 did (that spec deleted three lines
of live wiring; this one is not expected to delete anything).

It still gets a spec, for the reason spec 165 gave for itself: **the argument
is the artefact.** The issue does not ask for a fix — it asks a question
("is the absence intended, and where is that recorded") — and answering it
requires reading YARP's actual failure mode, the resilience-handler wiring,
the two existing precedents for the same *shape* of omission already in this
file, and ADR-0153's newly-recorded position on the one service that already
runs at more than one replica. A comment written without that trail would be
an assertion; a comment written after it is a decision closed out.

**Everything below §2 was verified at the tip of
`chore/2407-a-gateway-that-says-what-it-waits-for`, not taken from the issue.**
The issue is mine (Heiko's), and it says to verify rather than trust it — one
of its own claims does not survive that check (§2.4).

---

## 2. The premise, verified

### 2.1 The asymmetry itself

`src/AppHost/AppHost.cs:520-532` — `api-gateway` carries nine `.WithReference`
calls (eight context services plus `identity`) and **zero** `.WaitFor`. Every
one of those nine services declares its own `.WaitFor(rabbitmq)`,
`.WaitFor(keycloak)`, and (via the loop at `AppHost.cs:479-499`)
`.WaitForCompletion(migrations)`. `AppHost.cs` contains 44 `WaitFor` calls in
total; the issue's count is accurate.

### 2.2 What actually happens today when the gateway starts before a backend

**Destinations resolve per request, and the address is known immediately —
before the backend is listening.** `src/ApiGateway/Program.cs:54-56` wires
`AddReverseProxy().LoadFromConfig(...).AddServiceDiscoveryDestinationResolver()`,
resolving `http://camera-catalog` etc. through Aspire config-based service
discovery at request time. Spec 165 §2.2 already proved, for a different
resource, that `.WithReference` alone makes an env-var address resolvable with
no ordering attached — the same mechanism applies here: `api-gateway`'s
`services__<context>__http__0` keys are populated in its process environment
at launch regardless of whether the referenced service has started.

**A request to a not-yet-listening backend gets a fast, explicit failure, not
a hang or corruption.** `src/ApiGateway/appsettings.json`'s `ReverseProxy`
section configures nine routes and nine single-destination clusters and
declares **no `HealthCheck` block on any cluster** — confirmed by reading the
full file, not by absence of a grep hit. `src/ApiGateway/Program.cs` registers
no custom `IForwarderHttpClientFactory`; a repo-wide search for
`ForwarderHttpClientFactory` / `ActiveHealthCheck` / `PassiveHealthCheck`
under `src/ApiGateway` and `src/AppHost/AppHost.cs` returns no source hit.
YARP's default behaviour when the destination refuses the connection is to
catch the transport exception and answer the client with **502 Bad Gateway**.
There is no active health check to fail, no passive one to trip a circuit
breaker, and therefore nothing to get stuck: every request is an independent
connection attempt, so the very next request after the backend starts
listening succeeds — recovery is automatic and immediate, not something that
needs a restart or a cache to expire.

**The standard resilience handler does not reach this path.**
`src/ServiceDefaults/Extensions.cs:44-56`'s
`ConfigureHttpClientDefaults(http => http.AddStandardResilienceHandler(...))`
decorates `HttpClient`s created through `IHttpClientFactory`
(`AddHttpClient`/typed clients). YARP's forwarder builds its own
`HttpMessageInvoker` via `IForwarderHttpClientFactory`, a separate pipeline
that `ConfigureHttpClientDefaults` does not touch and that this gateway does
not override. So a proxied request that hits a dead destination is **not**
retried by the gateway itself — it fails once, fast, as a 502.

### 2.3 Does YARP have active health checks that would mark a destination
unhealthy and later recover it? — **No, and that is the right reading of it**

Covered in §2.2: no cluster in `appsettings.json` declares `HealthCheck`.
There is nothing to mark unhealthy and nothing that needs to recover, because
nothing was ever marked down in the first place — each request is judged on
its own connection attempt. This is a materially different (and simpler)
failure mode than "health check flaps, destination sits unhealthy for N
seconds after it is actually back," which is the scenario active health
checks exist to prevent and which does not arise here.

### 2.4 Is the issue's "and the SPAs retry" claim true? — **No, not generically**

The issue's own defensible-design paragraph says a 502 is fine "and the SPAs
retry." Checked against `apps/shared/src/api/gateway.ts:88-115`
(`gatewayBaseQuery`): the only retry it performs is **one** silent
session-renewal-and-replay, gated on `result.error.status === 401`
(spec 011 FR-011/012). A 502 or a network error takes neither branch — it
returns to the caller as a failed query, once. Repo-wide, no app configures
RTK Query's `retry()` utility, `setupListeners`, `refetchOnFocus`, or
`refetchOnReconnect`; the only `refetchOnMountOrArgChange` usages are two
edit-dialog hooks unrelated to startup races
(`apps/management-web/src/features/{layouts,overlays}/*EditorDialog.tsx`).

**This narrows the "it's fine" argument without breaking it.** What actually
bounds the blast radius is not browser-side retry — it is that:

1. Every SPA already races ahead of `api-gateway` itself: all three
   `AddNpmApp` calls (`AppHost.cs:571-620`) carry `.WithReference(apiGateway)`
   and no `.WaitFor(apiGateway)`. The gateway's own start-order independence
   from its backends is therefore not the first or the largest gap in this
   chain — the SPA-to-gateway gap is, and it is untouched by anything this
   issue could fix.
2. A 502 during the startup race is a symptom of a race that already exists
   one layer up, not a new failure mode this absence introduces.

Recorded because the issue asked to verify its own claims rather than trust
them, and this one does not hold as stated — the conclusion it was supporting
(the absence is defensible) still holds, on narrower grounds (§3).

### 2.5 Is `scripts/wait-for-e2e-stack.sh`'s 401 poll a workaround for the
missing `WaitFor`, or something a `WaitFor` could not do anyway?

Read in full at `scripts/wait-for-e2e-stack.sh:100-136`. It polls, in order:
migrations applied to all nine databases (hard fail if not), then `:5173`,
then `:5174`/`:5175` serving, then the gateway → camera-catalog route until it
answers **401** specifically (its own comment: "service + auth up, not 5xx").

**It is not reconstructing an ordering the composition should express — it is
proving something a `WaitFor` structurally cannot.** Aspire's own health gate
for `WaitFor`/"Running and healthy" is, for every project resource here, the
trivial unconditional check registered by
`src/ServiceDefaults/Extensions.cs:125-131` (`AddCheck("self", () =>
HealthCheckResult.Healthy(), ...)`) — ADR-0153 cites the same fact via #2125:
`/health` cannot fail. A `.WaitFor(cameraCatalog)` edge, had it existed, would
have resolved as soon as `camera-catalog`'s process reached `Running`, which
proves the process started — not that YARP's routing table loaded correctly,
that the CORS policy is attached, that the per-fab rate limiter is wired, or
that the request actually reaches `camera-catalog`'s auth middleware and gets
a real `401`. The script performs a live HTTP round trip through the whole
chain (browser origin → gateway CORS → rate limiter → YARP route → backend
auth pipeline) precisely because that is the only way to observe it. It would
have to stay exactly as it is whether or not this issue adds any `WaitFor`
edges — it is answering a question composition-level ordering cannot answer.

### 2.6 Prior art: two existing precedents for this exact shape, already in
this file

`AppHost.cs` already contains two `.WithReference(...)` calls to another
context's REST API with **no** `.WaitFor`, each with an inline reason:

- `AppHost.cs:361-365` (`stream-distribution` → `cameraCatalog`): *"Not a
  WaitFor — attribution must not gate host start, and an unreachable
  CameraCatalog simply leaves those streams unattributed and therefore
  invisible."*
- `AppHost.cs:399-402` (`layout-composition` → `cameraCatalog`): *"Not a
  WaitFor: a CameraCatalog outage must stop layout authoring only. Reading
  layouts, the hub pushes and video all carry on."*

Both are the same pattern this issue is asking about: a service reaches
another context over REST, the failure mode is a scoped, graceful
degradation rather than corruption or a stuck state, and the decision not to
gate startup on it is recorded as a comment at the call site rather than as
an ADR. `api-gateway`'s case is the same pattern at a larger scale — every
route degrades independently and recovers on the very next request — and has,
until now, been the one instance of the pattern left unrecorded.

---

## 3. What `.WaitFor` would actually change, and what it would cost

### 3.1 The transitive cost

Every one of the nine referenced services already carries
`.WaitFor(rabbitmq).WaitFor(keycloak)` plus `.WaitForCompletion(migrations)`,
and `migrations` itself waits for keycloak and all nine databases
(`AppHost.cs:317-330`). That chain is already the longest in the graph — it
is what every context service's own `Running` state is gated behind today.
Adding `.WaitFor` from `api-gateway` to all nine services would make the
gateway wait behind the slowest of those nine chains, i.e. it would become
one of the last resources in the entire stack to reach `Running`, on top of
whatever nine chains it would newly sit behind.

### 3.2 What it would buy — and the SPA gap it would not close

It would close the specific window described in §2.2: a request that reaches
`api-gateway` before a given backend is listening. It would **not** close the
larger window a developer actually experiences, because (§2.4) no SPA waits
for `api-gateway` either. A developer who opens `:5173` early can still reach
a gateway that has not started (`ECONNREFUSED` at the TCP layer, arguably a
worse, less informative failure than the fast 502 the current design
produces once the gateway itself is up) — closing only the gateway-to-backend
half of the chain does not deliver "no early failed request," it only moves
where in the chain the early failure happens. Delivering the full guarantee
would additionally require `.WaitFor(apiGateway)` on all three SPAs, which is
a different, unevidenced change with its own cost (§3.1's delay, again, now
gating dev-server availability) — exactly the kind of scope creep spec 165
§3.4 declined for the same reason: *"a differently-motivated addition;
adding it here would mix a deletion with a new ordering claim nobody has
evidence for."*

### 3.3 No evidence anything needs it

Repo-wide, nothing has been attributed to this absence: no filed defect, no
flaky-test report, no dashboard confusion recorded anywhere in `specs/**` or
`docs/adr/**` prior to this issue. The issue itself says so: "Nothing is
broken today... It is an unrecorded property of the composition, not an
outage."

**Conclusion: `.WaitFor` is not warranted.** It would cost the gateway's own
startup time (§3.1), it would not deliver the guarantee it appears to promise
without a second, unevidenced change (§3.2), and nothing today needs it
(§3.3). The absence is correct.

---

## 4. Does ADR-0153 bear on this?

**Yes, as context and as reinforcing evidence — not as something this change
must satisfy or alter.** ADR-0153 (Accepted 2026-09-16, PR #2415, not yet
merged to `develop`) names `api-gateway` as the one resource in the system
that already runs more than one instance, and records that its in-process,
unshared rate limiter is a **known-broken** consequence of that (#2283, open,
partition key is caller-controlled and the limiter has no shared store across
replicas).

**Interaction with `.WaitFor` and replicas, checked directly:** two gateway
replicas each independently evaluate their own `.WaitFor` chain — Aspire
schedules replicas of the same resource with the same wait annotations, so
each waits on the same nine dependencies in parallel with the other, not
sequentially or with any coordination between them. There is nothing here for
`.WaitFor` to interact badly with: it would not change which pod handles
which fab, would not touch the rate limiter's per-process state, and would
not make #2283 better or worse — that is a partitioning-and-storage defect,
orthogonal to start order. ADR-0153 explicitly assigns fixing it to #2283,
"not this ADR's [job]," and by the same logic not this issue's.

**What ADR-0153 does add to this issue's evidence:** it independently reached
the same posture this spec reaches for a different reason — replicas were
added to `api-gateway` for HA (#1005) "and its per-instance state was not
audited," and the ADR's own §Decision states plainly that "every service
already runs at one instance except `api-gateway`," treating the gateway as
already an acknowledged special case in this codebase rather than an
oversight to be quietly normalised. Adding `.WaitFor` here would not touch
any of ADR-0153's concerns; not adding it is consistent with ADR-0153 leaving
`api-gateway`'s specific defects to their own, separately-scoped fixes.

---

## 5. User story

**One story, P1.**

> **US1 — A developer reading `AppHost.cs` finds the gateway's absence of
> `WaitFor` explained, not silent.** As someone extending the composition, when
> I read the `api-gateway` registration, I can tell — without reconstructing
> YARP's failure mode from source — that the missing `WaitFor` edges are a
> deliberate, evidenced choice and not an oversight, in the same way the two
> existing `stream-distribution`/`layout-composition` precedents already tell
> me that for their own cross-context references.

**Independently shippable:** yes — one file, one comment, no consumer to
migrate, nothing else touched.

### 5.1 Acceptance scenarios

```gherkin
Scenario: a reader finds the decision recorded at the call site   # happy
  Given a developer reading AppHost.cs at the api-gateway registration
  When they reach the nine WithReference calls
  Then a comment at that block states that the absence of WaitFor is
    deliberate, names the failure mode (a fast 502, not corruption or a
    stuck state), and gives the reason (no active health check to trip,
    no evidence anything needs the edges, and the cost of adding it)

Scenario: the composition and every existing behaviour are unchanged   # regression
  Given the same run-mode Aspire stack
  When it boots before and after this change
  Then every resource's WaitFor/WithReference graph is byte-identical
  And `dotnet build -c Release` and the existing architecture/integration
    suites pass exactly as they did before

Scenario: auth                                                          # N/A
  Given this change adds, removes and alters no endpoint, scope or token
  Then there is no authorization behaviour to assert

Scenario: bad request                                                   # N/A
  Given this change accepts no input from any caller
  Then there is no request shape that can be malformed
```

---

## 6. Latency budget (constitution §IV) — **N/A**

No leg is affected. The change is a comment in dev-stack composition; no
request path, no push path, no production artefact. Stated rather than
omitted, per this file's own repeated instruction that an unwritten N/A is
indistinguishable from an unchecked one.

---

## 7. Out of scope, with reasons

| Item | Why not here |
|---|---|
| **`.WaitFor` from `api-gateway` to any of its nine references** | §3 — evidenced as not warranted, not merely undone by omission. |
| **`.WaitFor(apiGateway)` on the three SPAs** | A differently-motivated, unevidenced addition (§3.2) — the same reasoning spec 165 §3.4 gave for declining `.WaitFor(keycloak)` on `management-web`. |
| **Fixing #2283 (rate limiter partition key / shared store)** | ADR-0153 assigns it there explicitly; unrelated to start order (§4). |
| **Adding active health checks to the YARP clusters** | Would change runtime behaviour (destinations could be marked unhealthy and excluded), is new behaviour nobody asked for, and is a materially larger design question than this issue raises. |
| **A guard asserting the comment's presence or `AppHost.cs`'s text shape** | The exact "guards that read the design artefact" failure mode this repository has already catalogued (CLAUDE.md); see §8.3. |

---

## 8. Declarations

### 8.1 Which engineer — **`infra-engineer`, one agent**

`src/AppHost/AppHost.cs` is the only file touched. This is Aspire composition
judgement (what a missing `WaitFor` means, what YARP actually does on a dead
destination, how the two existing precedents are written) squarely in
`infra-engineer`'s brief. No `apps/**` or `src/ApiGateway/**` file changes —
§2's YARP/RTK-Query findings are read-only evidence gathered for this spec,
not a diff.

### 8.2 Whether a new ADR is needed — **No**

This spec does not propose a policy — it does not assert "gateway-shaped
resources shall never WaitFor their backends" as a rule binding future
resources. It records why **this one, already-existing absence** is correct,
using the same reasoning the file already applies twice elsewhere (§2.6) and
the same reasoning spec 165 already applied to this exact edge in its own §7
out-of-scope note. Per the brief that governs this spec: recording a decision
about one edge is implementation; declaring a policy about dev-stack ordering
in general would be a decision and a BLOCK. This spec does the former.

The one thing that would tip it into ADR territory — a general rule for when
a proxy-shaped resource may omit `WaitFor` — is exactly what §7 declines to
write, for the same reason spec 165 §8.2 declined it: no such rule is being
established, so there is nothing to record beyond this one call site.

### 8.3 Behaviour-changing or behaviour-preserving — **behaviour-preserving,
comment-only**

Unlike spec 165 (which removed three lines of live wiring alongside a
comment fix), **this change adds no code** — no `.WaitFor`, no `.WithReference`,
no resource, no environment variable. It is prose only. There is no
observable to move: the Aspire resource graph, the dev-stack boot order, and
every existing test's outcome are identical before and after.

**What phase 4a must produce:**

1. **No new automated test.** A test that asserts `AppHost.cs`'s text
   contains the new comment, or that the `api-gateway` resource carries no
   `WaitAnnotation`, would be a guard reading the design artefact rather than
   observing behaviour — it would prove the comment was typed, which the diff
   already proves, and nothing about the system. This is a **recorded
   exemption**, not a skip: CLAUDE.md requires phase 4a to produce something
   even when behaviour-preserving, and what it produces here is the
   characterisation baseline below, not a fabricated red/green pair.
2. **A characterisation baseline, observed green before and unmodified
   after:**
   ```sh
   dotnet build SmartSentinelEye.slnx -c Release
   dotnet test tests/Architecture.Tests
   dotnet test tests/Integration.Tests --filter "FullyQualifiedName~AppHostE2ESwitchTests"
   ```
   `AppHostE2ESwitchTests` is named explicitly, as spec 165 did, because it is
   the suite that composes the run-mode AppHost model; it must pass
   **unmodified**. An assertion that needs editing to keep passing would be
   evidence this change moved more than a comment — block, do not adjust.
3. **The exact comment text**, matched against the two existing precedents'
   voice (§2.6), is specified in `tasks.md` so the engineer is not asked to
   improvise the argument this spec already made.
