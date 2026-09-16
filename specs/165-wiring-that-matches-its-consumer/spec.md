# Spec 165 — Wiring that matches its consumer

**Issue:** #2405
**Branch:** `chore/2405-wiring-that-matches-its-consumer`
**Decisions of record:** **ADR-0152** (SignalR is the real-time transport) —
§Implementation Notes ("`management-web` does not [connect], and its `/hubs`
Vite proxy is dead configuration") is the decision this spec finishes applying.
Secondary: **ADR-0108** (browser e2e runs against a live `aspire run` stack),
**ADR-0037** (phases), **ADR-0144** (autonomous lane), **ADR-0109** (`[P]` on
disjoint files), **ADR-0086** (no `Co-Authored-By`).
**Predecessor:** `specs/164-a-record-that-matches-the-code/` §6 — which held
this out deliberately and filed it as this issue.
**Constitution:** §IV — checked and **N/A**; see §6.

---

## 1. Why, and why this needs a spec at all

Spec 164 removed `management-web`'s `/hubs` Vite dev proxy. That proxy was the
only consumer of `VITE_LAYOUT_HUB_ORIGIN` on the management side. Three lines in
`src/AppHost/AppHost.cs` still feed it.

**ADR-0037 permits "no spec — one line" for trivial changes, and this change is
three deleted lines.** It still gets a spec, for one reason: spec 164 declined
to make it *because it needed an argument that spec could not make*. Its §6 says
so in terms — removing a `WaitFor` changes the dev stack's startup graph, and
ADR-0152's Decision claims "nothing changes at runtime". A change deferred
because the argument was missing is not a change that can land without the
argument being written down. The diff is three lines; the reasoning is the
artefact.

**Everything below the premise section was verified at the tip of
`chore/2405-wiring-that-matches-its-consumer`, not taken from the issue.**

---

## 2. The premise, verified

### 2.1 The env var has no reader on the management side

| Claim | Evidence at tip |
|---|---|
| `apps/management-web` reads no environment in `vite.config.ts` | `apps/management-web/vite.config.ts` is 18 lines; it has no `process.env` read and no `server.proxy`. Its only Aspire mention is a comment. |
| `management-web` carries no SignalR dependency | `apps/management-web/package.json` dependency list has no `@microsoft/signalr`. |
| Nothing under `apps/management-web` names the hub | Zero hits across all 76 files for `@microsoft/signalr`, `signalr`, `/hubs`, `hubs/layouts`, `HubConnection`, `layout-composition`, `layoutComposition`, `LAYOUT_HUB`, `services__layout-composition`. |
| Nothing management-web *imports* reaches the hub client | The SignalR client is `apps/shared/src/realtime/layoutHub.ts`, exposed only at the explicit subpath `./realtime/layoutHub`. management-web imports 28 distinct `@smart-sentinel-eye/shared/*` specifiers; neither `realtime/layoutHub` nor `realtime/hubUrl` is among them. No other shared module imports `realtime/`, so there is no transitive reach. |
| No other reader exists anywhere | Repo-wide (`--hidden --no-ignore`), `VITE_LAYOUT_HUB_ORIGIN` occurs in exactly two executable places: `apps/kiosk-web/vite.config.ts:24` and `AppHost.cs:580/593/615`. Everything else is `specs/**` prose. **No `.env*` file exists in this repository at all.** No `docker-compose*`; the only `Dockerfile` is `src/AppHost/mosquitto/Dockerfile`; `deploy/helm/**` holds two Mosquitto files; `.github/workflows/ci.yml` has no hit for `LAYOUT_HUB`, `layout-composition`, `signalr` or `/hubs`; root `package.json` scripts have none; `playwright.config.ts` has no `globalSetup` and no `webServer`. |

**The nearest false positive, named so a later reader does not rediscover it as
a blocker:** `apps/shared/src/api/layouts.api.ts:80` calls
`gatewayBaseQuery('layout-composition/layouts')`, and management-web *does*
import it. That is a **REST path prefix on the YARP gateway**, resolved from
`VITE_API_GATEWAY_URL` (`apps/shared/src/api/gateway.ts:16`, whose comment says
realtime and WebRTC do not go through it). It depends on
`.WithReference(apiGateway)`, which this spec does not touch.

### 2.2 `WithReference`'s only job here was to make that env var resolvable

For an `AddNpmApp` resource, `.WithReference(layoutComposition)` injects the
service-discovery keys `services__layout-composition__http__0` / `__https__0`
into the process environment. Nothing else. There is no connection string and no
typed client.

`kiosk-web/vite.config.ts:23-26` reads those raw keys **as a fallback** behind
`VITE_LAYOUT_HUB_ORIGIN`. `management-web` reads neither. So for management-web
the reference produces two environment variables that no process reads.

**`WithReference` is not an ordering edge.** The in-repo proof is commit
`7e2b2db4` ("chore(apphost): wait for layout-composition before starting the
SPAs"), which added `.WaitFor(layoutComposition)` to resources that had carried
`.WithReference(layoutComposition)` since `ce1eb753`. If the reference had
sequenced anything, that commit would not have existed. Removing the reference
therefore changes environment content only; the startup-graph question is
entirely about the `WaitFor`.

### 2.3 kiosk-web's identical wiring is live and must not be touched

- `apps/kiosk-web/package.json:15` — `"@microsoft/signalr": "10.0.11"`.
- `apps/kiosk-web/vite.config.ts:40-42` — `proxy: { '/hubs': { target, ws: true, … } }`. **The only `/hubs` proxy in the repository.**
- `apps/kiosk-web/src/features/revocation/useLayoutLifecycle.ts:15,86` — imports `createLayoutHubClient` and opens `/hubs/layouts`.

`kiosk-wall` is the **same bundle** (`AddNpmApp("kiosk-wall", "../../apps/kiosk-web", "dev")`), so it reads the same config file and needs the same three lines.

### 2.4 The comment block is shared, and spec 164 already corrected its prose

The block at `AppHost.cs:542-572` precedes `management-web`'s call and serves all
three. Spec 164 already rewrote it to name the kiosk as the consumer, and it
already states the defect verbatim:

> `VITE_LAYOUT_HUB_ORIGIN` is the shell-safe alias **the kiosk app** uses to
> build its `/hubs` dev proxy (management-web's vite.config.ts reads no
> environment and has no such proxy — **its reference below is dead wiring,
> tracked by #2405**).

Two sentences in it go stale the moment the three lines are deleted: that
parenthetical, and `WaitFor(layoutComposition) is ordering hygiene — the
kiosk's vite.config.ts reads the environment once…`, which after this change
describes only resources it sits above. **The comment is therefore a fourth
edit, not a drive-by.** It is the same file and the same block; leaving it would
reinstate exactly the defect spec 164 existed to remove.

---

## 3. What removing `.WaitFor` actually changes

### 3.1 The concrete, observable difference

`.WaitFor(layoutComposition)` holds `management-web` in `Waiting` until
`layout-composition` reaches Running-and-healthy. It is **management-web's only
`WaitFor`**: `.WithReference(apiGateway)` and `.WithReference(keycloak)` carry no
ordering. So after the change, management-web has **no start-order dependency at
all** and its Vite dev server launches as soon as DCP schedules it.

Because `layout-composition` itself declares `.WaitFor(rabbitmq).WaitFor(keycloak)`
(`AppHost.cs:403-404`), today's edge **transitively** delays management-web
behind RabbitMQ and Keycloak. That transitive delay is removed too. **This is
the only consequence of the change that is not purely a deletion of dead
wiring, and §5 is where it gets paid for.**

Observable in the Aspire dashboard: management-web reaches `Running` before
`layout-composition` instead of after, and its `Waiting` state disappears.

### 3.2 Nothing in the dev flow depends on the edge

| Candidate dependency | Finding |
|---|---|
| **An API call on first render** | management-web's REST goes to the gateway (`VITE_API_GATEWAY_URL`), which is a different reference. `api-gateway` itself declares **no `WaitFor` of any kind** (`AppHost.cs:521-539`), so the repo's existing position is already that the REST path is not start-ordered. |
| **A health gate** | management-web has no health check; `AddNpmApp` resources here declare none. |
| **The integration fixture** (`tests/Integration.Tests/Fixtures/AspireFixture*`) | **Structurally cannot depend on it.** The fixture boots with `E2ETests=true` (`AspireFixture.cs:246`), and all three npm apps live inside `if (isRunMode && !isE2ETests)` (`AppHost.cs:547`). management-web does not exist in that stack. |
| **The e2e harness** (ADR-0108) | `playwright.config.ts` has no `globalSetup` and no `webServer` — Aspire owns orchestration. Readiness is `scripts/wait-for-e2e-stack.sh`, which gates on its **own** sequence: migrations applied in all nine databases (hard fail), then `:5173`, then `:5174` and `:5175`, then the gateway→camera-catalog probe until it answers **401** (auth up). None of those probes reads the start order of management-web against layout-composition, and the auth-readiness gate that matters is asserted *after* :5173 regardless. |
| **A model-shape test** | `tests/Integration.Tests/AppHostE2ESwitchTests.cs:144` asserts `names.ShouldContain("management-web")`. It reads **resource names only** — no references, no wait annotations. Unaffected. |
| **A guard reading `AppHost.cs` as text** | Two exist (`ContainerImagePinTests`, `FoundingDecisionRecordTests`); neither mentions the hub, the env var or the wait. |

### 3.3 Was the wait load-bearing for a reason nobody wrote down?

**No — and this is the one question where the honest answer is better than
expected.** The reason *is* written down, in the commit that introduced it.
`7e2b2db4` added `.WaitFor(layoutComposition)` to both SPAs with this comment:

> `WaitFor(layoutComposition)` is load-bearing, not just ordering polish: each
> app's `vite.config.ts` reads `services__layout-composition__http__0` ONCE, at
> config-evaluation time, to build the `/hubs` proxy. If Vite boots before the
> endpoint resolves, the proxy is silently omitted for the life of the process
> and every `/hubs/layouts/negotiate` 404s forever — the kiosk then sits on a
> permanently-stuck "live updates degraded" badge (spec 011 FR-006/FR-010).

That reason was **accurate for management-web when it was written**:
`apps/management-web/vite.config.ts` carried a `/hubs` proxy from `aef32005`
("fix(web): proxy the SignalR layout hub to layout-composition in dev") until
`0fe3c1b8` ("chore(management-web): drop the dead `/hubs` dev proxy", spec 164).
The wait was added for a stated cause, the cause was real, and spec 164 removed
the cause. Nothing here is a symptom-fix whose motive was lost.

**What is missing, stated so it is not mistaken for proven:** nobody has
recorded a *second* reason. The argument for removal is that the only recorded
reason is gone and §3.2 finds no other consumer — it is not proof that no
unrecorded reason ever existed. §3.1's transitive delay is the concrete thing
that was being bought incidentally, and §5 is the evidence that buys the
confidence back.

### 3.4 The one thing that is genuinely given up

In dev, a developer who opens `:5173` seconds after `aspire run` can now reach
the OIDC redirect before Keycloak is Running. Today's transitive wait made that
unlikely.

**This spec does not replace the edge with `.WaitFor(keycloak)`**, for three
reasons, and it records them rather than leaving the omission to look like an
oversight:

1. It is a **different change with a different motive** — naming a real
   dependency, not deleting a dead one. Mixing them is exactly what the
   smallest-possible-change rule forbids.
2. The repository's existing position is already that the REST/auth path is not
   start-ordered: `api-gateway`, which every REST call traverses, has no
   `WaitFor` at all.
3. Adding it on a guess would re-create this issue's own failure mode — wiring
   added for a symptom, outliving its reason.

If §5's observation shows the boot is genuinely worse for a developer, that is a
finding to file, not a line to slip into this diff.

---

## 4. User story

**One story, P1, and there is only one.**

> **US1 — A developer reading `AppHost.cs` finds no wiring that serves nothing.**
> As someone extending the dev stack, when I read the three `AddNpmApp` calls, I
> can tell which app consumes the layout hub from the wiring alone, without
> having to open two `vite.config.ts` files to discover that one of the three
> references is inert.

**Independently shippable:** yes — one file, four edits, no consumer to migrate.

### 4.1 Acceptance scenarios

Gherkin covering the four required shapes. *Auth* and *bad-request* have no
surface here (no endpoint, no request, no caller), and saying so explicitly is
the point rather than inventing one.

```gherkin
Scenario: the dev stack boots with management-web no longer waiting   # happy
  Given a run-mode Aspire stack booted from the branch tip
  When every resource has settled
  Then management-web reports Running
  And management-web never entered a Waiting state for layout-composition
  And layout-composition, kiosk-web and kiosk-wall all report Running

Scenario: the kiosk's live updates still work                          # conflict-adjacent
  Given that stack
  When a browser loads kiosk-web on :5174
  And a layout lifecycle change is broadcast
  Then /hubs/layouts/negotiate does not 404
  And the kiosk does not sit on the "live updates degraded" badge

Scenario: management-web is unchanged in what it can do                # regression
  Given that stack
  When a browser loads management-web on :5173 and signs in
  Then the layouts list loads through the gateway
  And no request to /hubs is made from that origin

Scenario: auth                                                          # N/A
  Given this change adds, removes and alters no endpoint, scope or token
  Then there is no authorization behaviour to assert
  And the scope catalogue and RequireScope declarations are untouched

Scenario: bad request                                                   # N/A
  Given this change accepts no input from any caller
  Then there is no request shape that can be malformed
```

---

## 5. How this is verified

### 5.1 Phase 4a's colour — argued, not adopted

The brief's instinct was "behaviour-preserving for production, behaviour-changing
for the dev stack". That splits the change in a way the repository's two colours
do not, so it needs resolving rather than restating.

**Per item:**

| Item | Colour | Why |
|---|---|---|
| `.WithEnvironment("VITE_LAYOUT_HUB_ORIGIN", …)` on management-web | **Behaviour-preserving** | Removes two-to-one environment variables no process reads (§2.1). Nothing observes them. |
| `.WithReference(layoutComposition)` on management-web | **Behaviour-preserving** | Removes the `services__layout-composition__*` keys, also unread, and creates no ordering edge (§2.2). |
| `.WaitFor(layoutComposition)` on management-web | **Behaviour-changing — for the dev and e2e stacks only** | The start order observably differs (§3.1). |
| The comment block | **Behaviour-preserving** | Prose. |

**The declared colour for phase 4a is therefore behaviour-changing**, because
CLAUDE.md's rule is that ambiguity resolves to red and this is not even
ambiguous — one of the four items moves an observable.

**And that immediately produces the honest problem:** a behaviour-changing item
must have a test observed red first, and **there is no red to observe**. The
changed behaviour is Aspire's start ordering of a dev-only resource. Writing an
xUnit test that composes the AppHost model and asserts "management-web carries no
`WaitAnnotation` for layout-composition" would be **a guard that reads the design
artefact** — this repository has recorded that failure mode by name, and such a
guard would prove the annotation was deleted, which the diff already proves, and
nothing about whether the stack boots.

**Resolution, and it is the substantive judgement in this spec:** phase 4a
produces **no new automated test**, and that is a deliberate, argued exemption
recorded here rather than a skip. CLAUDE.md says phase 4a may not be skipped —
so it is not skipped; it is discharged with the evidence the change actually
admits:

- **A characterisation baseline, observed green before the change** (§5.2). The
  existing suites are the regression net for everything this could break.
- **An observed boot, before and after** (§5.3). This is the evidence for the
  behaviour-changing item, and it is a phase-5 observation promoted into the
  phase-4a slot because it is the only thing that can witness a start-order
  change.

If a reviewer disagrees and wants a model-shape assertion, that is a finding to
argue in phase 6 — but it should be argued against the named anti-pattern, not
added reflexively.

### 5.2 Characterisation baseline (before the change, green)

Captured verbatim, all green before anything is edited:

```
dotnet build SmartSentinelEye.slnx -c Release
dotnet test tests/Architecture.Tests
dotnet test tests/Integration.Tests --filter "FullyQualifiedName~AppHostE2ESwitchTests"
```

`AppHostE2ESwitchTests` is named explicitly because it is the only suite that
composes the run-mode AppHost model, and it must pass **unmodified** afterwards.
An assertion that has to be edited is evidence the behaviour moved further than
this spec claims — block, do not adjust.

### 5.3 The independent end-to-end procedure (the real evidence)

Run **before** the edit, then **after**, on one machine, and keep both outputs.
One Aspire stack at a time.

1. `aspire run` (or `dotnet run --project src/AppHost/SmartSentinelEye.AppHost.csproj`).
2. In the dashboard, record for `management-web`, `kiosk-web`, `kiosk-wall` and
   `layout-composition`: whether each entered **Waiting**, and the order in
   which they reached **Running**.
   - *Before:* management-web is expected to show Waiting, and to reach Running
     after layout-composition.
   - *After:* management-web must show **no Waiting** for layout-composition,
     and may reach Running before it. **kiosk-web and kiosk-wall must still
     show Waiting and must still start after layout-composition** — this is the
     assertion that the shared block was not over-edited.
3. Open `http://localhost:5173`, sign in, load the layouts list. It must render.
   Record whether any error was seen at the OIDC redirect (§3.4's given-up
   property; a failure here is a finding, not a silent pass).
4. Open `http://localhost:5174`. With DevTools Network open, confirm
   `/hubs/layouts/negotiate` returns **200, not 404**, and that the kiosk does
   not settle on the "live updates degraded" badge. **This is the
   non-negotiable check** — it is the one the shared comment block warns about
   and the one a careless edit breaks.
5. In management-web's DevTools, confirm **no request to `/hubs`** is made.

The full Playwright suite is *not* required: CI's e2e job boots the same
run-mode stack behind `scripts/wait-for-e2e-stack.sh` and will exercise all
three front ends on the PR. The manual boot exists because the CI job's
readiness script gates on its own probes (§3.2) and would pass identically
whether or not the start order changed — so it confirms nothing broke but
cannot witness the thing that changed.

---

## 6. Latency budget (constitution §IV) — **N/A**

No leg is affected. The change touches dev-stack composition only; no
production artefact, no request path, no push path. The `Event → overlay state`
leg carries the SignalR push this wiring nominally concerned, and that push is
kiosk-only and untouched — the kiosk keeps all three lines.

Stated rather than omitted because §IV's obligation attaches per change, and
"N/A" that nobody wrote is indistinguishable from "nobody checked".

---

## 7. Out of scope, with reasons

| Item | Why not here |
|---|---|
| **`.WaitFor(keycloak)` on management-web** | §3.4. A differently-motivated addition; adding it here would mix a deletion with a new ordering claim nobody has evidence for. |
| **`.WaitFor` on `api-gateway`** | Same shape, larger blast radius, and no evidence anything needs it. Noticed while establishing §3.2's baseline; not this issue's subject. |
| **The three lines on `kiosk-web` / `kiosk-wall`** | Live and load-bearing (§2.3). Touching them breaks the kiosk's `/hubs` proxy — the exact failure `7e2b2db4` documents. |
| **A SignalR backplane** | ADR-0152 §4's scale-out defect. Its own issue; unrelated to dev-stack wiring. |
| **A guard that fails when an Aspire resource injects an environment variable nothing reads** | It would have caught this, and nothing today can. But it is **new behaviour**, this issue did not ask for it, and it is a substantial piece of design (it must model Vite's `import.meta.env` substitution to avoid false positives). Worth proposing separately; not smuggled in. |
| **Observing SignalR's SSE fallback** | ADR-0152 declines to rely on it; nothing here does either. |

---

## 8. Declarations

### 8.1 Which engineer — **`infra-engineer`**, one agent

`src/AppHost/AppHost.cs` is the only file edited. Aspire composition is
`infra-engineer`'s brief, and the judgement the change needs — what a `WaitFor`
buys, what the dashboard should show afterwards, whether the e2e readiness
script depends on the edge — is orchestration judgement, not TypeScript.

The verification in §5.3 opens two browsers, which looks like frontend work. It
is not: the assertions are *the stack booted* and *the proxy still resolves*,
both properties of the composition. No `apps/**` file is touched and no
component is read.

### 8.2 Whether a new ADR is needed — **No**

**Checked against the gate that just blocked #2404, not waved past.**

The decision this implements already exists. ADR-0152 §Implementation Notes:
"`LayoutLifecycleHub.cs:8` claims 'the kiosk (and management-web) connects';
`management-web` does not, and its `/hubs` Vite proxy is dead configuration."
Spec 164 §6 files the AppHost half as this issue rather than as a new question.
Deleting wiring whose consumer an accepted ADR has already declared absent is
implementation.

**The one thing that would have made it an ADR, and why it does not:** if
removing `.WaitFor` required deciding a *policy* — "dev-stack resources shall/
shall not be ordered against their service dependencies" — that is a decision,
and per the brief a BLOCK rather than a judgement call. It does not, because this
spec declines to set any such policy (§3.4, §7). It removes one edge whose single
recorded reason was itself removed by spec 164, and leaves every other ordering
edge in the file exactly as it is — including the `api-gateway` case that the
same policy would have governed. No rule is established, so there is nothing to
record.

ADR-0152's "nothing changes at runtime" is not contradicted: that sentence is
about production behaviour, and it was spec 164's claim about *itself* that
forced the deferral. This spec makes no such claim — §3.1 states the change
plainly.

### 8.3 Behaviour-changing or behaviour-preserving — **per item, in §5.1**

Summary: three of four items behaviour-preserving, the `.WaitFor` removal
behaviour-changing for the dev and e2e stacks. **Declared colour:
behaviour-changing.**

**What phase 4a must produce:**

1. The characterisation baseline of §5.2, **observed green before the change**,
   output saved verbatim.
2. The **before** half of §5.3's boot observation, recorded.
3. **No new automated test**, for the reason argued in §5.1. The engineer must
   not manufacture a red by asserting the absence of a string in `AppHost.cs`.

**What the PR body must quote:** the §5.2 baseline output, and both halves of
§5.3's dashboard observation side by side — specifically that management-web's
Waiting state is present before and absent after, and that kiosk-web's is
present in both.
