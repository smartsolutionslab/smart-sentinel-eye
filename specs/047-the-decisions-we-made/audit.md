# Audit: the founding decisions against the system that exists

**Feature**: `047-the-decisions-we-made` · **Method**: [research.md](./research.md) · **Vocabulary**: [data-model.md](./data-model.md)

**The unit is the claim, not the decision.** A row with three claims gets three
verdicts. Decision 005 is the worked example and it is the reason this rule
exists.

## Verdicts

| Verdict | Applies when |
|---|---|
| **Holds** | Every part of the claim is true of the repository today. |
| **Diverges** | The job is done — differently, or under another name — than the row says. The row is wrong; the system may be right. |
| **Not built** | Neither the named thing nor anything doing its job exists. **Requires two failed searches**, both recorded. |
| **Unverifiable here** | The claim is about deployment, hardware or network topology this repository cannot settle either way. |

**Evidence is the command and its result, never a conclusion.** The claims being
audited are themselves conclusions somebody recorded without evidence, which is
why this document exists.

---

## The three calibration rows (T002)

All three assign work to **StreamKeeper**, whose name appears nowhere in the
code. **All three take different verdicts.** Read these before auditing anything
else.

### 005 — camera protocols and vendor adapters → **Diverges (partly)**

> *Camera protocols: RTSP + ONVIF (Profile S/T) on day one. Adapter pattern in
> StreamKeeper for vendor-specific drivers (Axis VAPIX, Hikvision, Bosch,
> Hanwha…) later, without touching the core.*

| Claim | Verdict | Evidence |
|---|---|---|
| RTSP on day one | **Holds** | MediaMTX pulls camera paths over RTSP; `rtspAddress` in `src/AppHost/Resources/*.yml`, `runOnDemand` FFmpeg publishes to `rtsp://` |
| ONVIF (Profile S/T) on day one | **Not built** | name: `grep -ril "onvif" src/ --include=*.cs` → no matches. job: no discovery, no device-management, no PTZ client anywhere |
| Adapter pattern in StreamKeeper for vendor drivers | **Not built** | name: `grep -ril "vapix\|hikvision\|bosch\|hanwha" src/ --include=*.cs` → no matches. job: no adapter seam; `StreamDistribution` speaks only to MediaMTX's HTTP API |

**Disposition**: correct — issue. ONVIF is named as *"on day one"* and is absent
entirely, which is a gap rather than a divergence.

### 012 — SFU scaling and the coordinator → **Not built**

> *StreamKeeper scaling = horizontal shard-by-camera. Coordinator service
> (Raft/etcd-class consistency) owns cam→SFU ownership map. Clients query the
> coordinator. Failover under 5 s.*

| Claim | Verdict | Evidence |
|---|---|---|
| Horizontal shard-by-camera | **Not built** | name: `grep -ril "shard" src/ --include=*.cs` → no matches. job: `AppHost.cs` runs **one** SFU (plus a dev-only `camera-sim`); nothing distributes cameras across instances |
| Coordinator service, Raft/etcd-class | **Not built** | name: `grep -ril "coordinator" src/` → no matches. job: no consensus library, no ownership map, no leader election |
| Failover under 5 s | **Not built** | `grep -in "failover" src/AppHost/AppHost.cs` → one hit, an unrelated comment about a 90 s timeout |

**Not contradicted so much as unreached**: with one SFU there is nothing to
shard between. **Disposition**: correct — issue, for the 250-camera target.

### 013 — network topology → **Unverifiable here**

> *Network = cameras on isolated OT VLAN. StreamKeeper is dual-NIC and the only
> bridge to the IT VLAN. No other service touches a camera directly.*

| Claim | Verdict | Evidence |
|---|---|---|
| Cameras on an isolated OT VLAN | **Unverifiable here** | A fab network property. Nothing in this repository can confirm or refute it |
| Dual-NIC bridge | **Unverifiable here** | Deployment topology |
| No other service touches a camera directly | **Holds** | Only MediaMTX opens RTSP to cameras; `StreamDistribution` speaks to MediaMTX's API, never to a camera |

**Recording these as "not built" would be a false statement about a fab nobody
here can see.** The third claim *is* checkable in code, and holds — which is why
the row is split rather than dismissed.

---

## Decisions 001–009 (T003)

### 001 — stack and modelling → **Holds**

| Claim | Verdict | Evidence |
|---|---|---|
| Frontend = React, browser-only | **Holds** | `apps/kiosk-web`, `apps/management-web`, both with a `react` dependency; no native client |
| Backend = .NET / C# | **Holds** | 9 context projects, `net10.0` |
| DDD with value objects throughout | **Holds** | `src/Shared.Kernel/Primitives/IValueObject.cs`, `IStronglyTypedId.cs`; enforced by `tests/Architecture.Tests` |
| CQRS / ES only where justified | **Holds** | Hand-rolled command/query handlers; no event sourcing anywhere — consistent with "only where justified" |

### 002 — scale and recording → **Holds / unverifiable**

| Claim | Verdict | Evidence |
|---|---|---|
| 20-camera pilot, 250-camera target | **Unverifiable here** | A programme target, not a repository property |
| Recording out of scope for v1 | **Holds** | No recording, segmenting or DVR code in `src/` |
| Architecture must not preclude recording | **Unverifiable here** | A judgement about future work, not a checkable claim |

### 003 — workflow → **Holds**

| Claim | Verdict | Evidence |
|---|---|---|
| Spec-Kit (`specify`/`plan`/`tasks`) | **Holds** | `.specify/templates/`, 47 spec folders |
| GitHub Project board | **Holds** | Project #13, ~450 items |

*Note, not a defect: CLAUDE.md's Phase 3 description of the board gate was wrong
for sixteen specs and was corrected on 2026-08-28. The decision itself holds.*

### 004 — superseded by 015 → **n/a**

Marked superseded in the row itself. Not audited; 015 is audited in its place.

### 005 — see calibration above → **Diverges (partly)**

### 006 — deployment posture → **Holds (partly) / unverifiable**

| Claim | Verdict | Evidence |
|---|---|---|
| On-prem first, no cloud dependency in v1 | **Holds** | Every runtime resource is a local container in `AppHost.cs`; no cloud SDK or endpoint |
| Hybrid cloud control plane is the v2 differentiator | **Unverifiable here** | A statement about v2 intent |
| Idempotent IDs | **Holds** | Guid v7 identifiers (`CreateVersion7`) throughout, per ADR-0039 |
| Versioned config so cloud sync is additive | **Unverifiable here** | No cloud sync exists to test the property against |

### 007 — identity → **Holds**

| Claim | Verdict | Evidence |
|---|---|---|
| Keycloak per fab, self-hosted OIDC | **Holds** | 34 references in `AppHost.cs`; realm imported from `src/AppHost/Realms/` |
| v2 federation | **Unverifiable here** | v2 intent |

### 008 — kiosk authentication → **Diverges**

> *Kiosk auth = device-bound credential → OIDC `client_credentials` → short-lived
> token, view-only scope.*

| Claim | Verdict | Evidence |
|---|---|---|
| Kiosk uses `client_credentials` | **Diverges** | `smart-sentinel-eye-realm.json`, client `kiosk-web`: `"publicClient": true`, `"standardFlowEnabled": true`, `"directAccessGrantsEnabled": false`, **no service account**. It uses the authorization-code flow |
| Device-bound credential | **Built, and unused by the app** *(corrected — see below)* | `EnrollKioskCommandHandler.cs` creates a per-kiosk **confidential** client (`ServiceAccountsEnabled: true`, `PublicClient: false`, `StandardFlowEnabled: false`) and `POST /kiosks/enroll` returns a single-reveal secret. **But `apps/kiosk-web` signs in as the shared public `kiosk-web` client via auth-code and never uses an enrolled credential** (issue 1976) |
| View-only scope | **Holds** | Kiosk token carries read scopes only; asserted in `e2e/kiosk-identity.spec.ts` |
| Operators use auth-code flow and bind to a kiosk | **Partly** — auth-code holds; **binding not built** | `smart-sentinel-eye-web` uses the code flow; no kiosk-binding mechanism exists |
| No PTZ without an operator token | **Unverifiable here** | PTZ is not built at all, so the constraint has nothing to bind |

**Disposition**: correct — issue 1976. The row describes a design that **was**
taken, in the Identity context, and that the kiosk app does not use.

> **This verdict was wrong when first recorded, and the error is instructive.**
> The original check read the realm's `kiosk-web` browser client, saw
> `publicClient: true` with no service account, and concluded the device-bound
> credential did not exist. **The second search this audit's own method requires
> — for the *job*, under any other name — was not run.** The enrolment handler
> was one grep away. Found in code review; recorded here rather than quietly
> amended, because an audit that hides its own misses is worth less than one
> that shows them.

### 009 — persistence and infrastructure → **Mixed, and one systemic finding**

| Claim | Verdict | Evidence |
|---|---|---|
| PostgreSQL as the default | **Holds** | `timescale/timescaledb` container; every context persists to Postgres |
| Marten where invariants justify it | **Not built** *(unrealised intention)* | name: `grep -ril "marten" --include=*.csproj` → no package reference; the only source hit is a comment in `Camera.cs`. job: no event sourcing anywhere. **"Not yet justified anywhere" is a fair reading** — recorded as unrealised, not false |
| **Prometheus for metrics** | **Not built — and contradicted by an accepted ADR** | name: only occurrence in `src/` is a *comment* in `mediamtx.yml` about MediaMTX's own exposition format. job: no Prometheus container in `AppHost.cs`, no exporter package. **ADR-0118 abandoned the Grafana/Prometheus stack and chose the Aspire dashboard as the single sink** |
| MinIO as the future object store | **Diverges** *(corrected)* | Declared in `AppHost.cs` — and **"future" is wrong**: `MinioAuditChunkArchiver.cs` calls `PutObjectAsync`, driven by `AuditRetentionHostedService`. Objects are archived today (ADR-0101). Missed on the first pass by reading the decision rather than looking for a writer |
| No EventStoreDB | **Holds** | `grep -ril "eventstore" src/ tests/ --include=*.cs --include=*.csproj` → no matches |

**The Prometheus claim is not confined to this row.** The constitution repeats it
three more times, and nobody propagated ADR-0118:

- **§AppHost (line ~209)** — lists *"Prometheus, the React app"* among resources
  *"declared in the `AppHost` project"*. **There is no Prometheus resource.**
- **§Stack (line ~291)** — *"**Prometheus** for metrics."*
- **§Retention (line ~346)** — *"Metrics: **30 days** in Prometheus; long-term in
  Thanos/Mimir"*. Neither Prometheus, Thanos nor Mimir exists.

**Disposition**: legitimise — ADR. ADR-0118 already decided this; the record
simply never followed. Same shape as §IX's observability row.

---

## Running tally

| Range | Holds | Diverges | Not built | Unverifiable |
|---|---|---|---|---|
| 001–009 (+ calibration 012, 013) | 12 | 3 | 8 | 8 |

**Rows recorded as holding are recorded with evidence**, deliberately — an audit
listing only its discoveries cannot be distinguished from one that stopped when
it got bored.

---

## Decisions 010–018 (T004)

*012 and 013 are audited in the calibration section above.*

### 010 — messaging → **Holds**

| Claim | Verdict | Evidence |
|---|---|---|
| RabbitMQ for internal service-to-service messaging | **Holds** | 24 references in `AppHost.cs`; Wolverine transports per ADR-0042 |
| RabbitMQ for external system ingress | **Holds** | `EventIngestion` consumes from RabbitMQ; MQTT ingress bridges into it |
| REST API for external publishers, translating to publishes | **Holds** | `EventIngestion` HTTP ingress endpoints |
| Plan for Streams plugin if replay is required | **Holds** *(as written)* | The claim is conditional intent, not a present-tense assertion. No streams plugin exists, and none is claimed |

### 011 — transcoding → **Holds (partly) / not built**

| Claim | Verdict | Evidence |
|---|---|---|
| Passthrough when the profile is WebRTC-compatible | **Holds** | `-c copy` in `CameraSimProvisioner`; MediaMTX republishes H.264 without re-encoding |
| GPU transcode (NVENC / Quick Sync) when forced | **Not built** *(unrealised intention)* | name: `grep -ril "nvenc\|quicksync\|qsv" src/` → no matches. job: no GPU pipeline, no fallback branch. **"Only when forced" has never been forced** — recorded as unrealised, not false |
| ~1 NVENC-class GPU per 50–100 transcodes | **Unverifiable here** | A sizing budget for hardware that does not exist |

### 012, 013 — see calibration above

### 014 — video-wall sync → **Amended (ADR-0128, spec 045)**

Already corrected. Recorded as amended rather than re-audited.

### 015 — the latency SLO → **Holds (partly), one claim corrected elsewhere**

| Claim | Verdict | Evidence |
|---|---|---|
| End-to-end SLO ≤ 800 ms | **Holds** | Constitution §IV states it; the six sub-budgets are recorded there |
| The six sub-budgets (80/120/200/200/50/150 ms) | **Holds** | §IV's table matches this row exactly |
| WebRTC still the streaming protocol | **Holds** | WHEP over `RTCPeerConnection`; MediaMTX as SFU |
| **"frame-synced"** | **Diverges** | The overlay is a DOM label layered over the video, not paired with a frame. **Spec 046 is correcting this separately** — recorded here, not duplicated |

### 016 — the nine bounded contexts → **Holds**

| Claim | Verdict | Evidence |
|---|---|---|
| Nine contexts, as named | **Holds** | Exactly nine context projects: `AuditObservability`, `Automation`, `CameraCatalog`, `EventIngestion`, `Identity`, `LayoutComposition`, `OverlayDesigner`, `StreamDistribution`, `SystemVariables` — the row's list, one for one |
| Camera Catalog ≠ Stream Distribution | **Holds** | Separate projects, separate lifecycles; `Architecture.Tests` forbids a project reference between them |
| Variables + Events + Overlays are three separate contexts coupled by RabbitMQ | **Holds** | Three projects; cross-context traffic only via `Shared.Contracts` |
| Browser overlay engine reads from a per-kiosk WebSocket gateway | **Holds** | The layout hub; ADR-0076 |

**Recorded in full despite holding**, because this is the row that calibrates the
result: the reconnaissance found real problems *and* rows that are simply right.

### 017 — system variable types → **Diverges**

> *v1 = `text`, `boolean`, `integer`, `decimal`, `datetime`, `json` (opaque).*

**Six types are declared. Three exist.**

| Claim | Verdict | Evidence |
|---|---|---|
| `text` | **Holds** *(renamed)* | `VariableType.String` |
| `boolean` | **Holds** | `VariableType.Boolean` |
| `integer` and `decimal` as distinct types | **Diverges** | Both collapsed into a single `VariableType.Number`. `From()` accepts exactly `String \| Number \| Boolean` and throws otherwise |
| `datetime` | **Not built** | name: no `DateTime` member in `VariableType.cs`. job: `From()` throws on anything outside the three — there is no path by which a datetime variable can exist |
| `json` (opaque), parsed client-side | **Not built** | Same evidence. No JSON variable type, so nothing forwards or parses one |
| Each variable declares exactly one type | **Holds** | One `VariableType` per variable |
| Promoting `json` to `json + schema` reserved for later | **n/a** | Reserved future work for a type that does not exist |

**Disposition**: correct — issue. Two named v1 types are absent, and `integer`/
`decimal` were merged without a record. This is a divergence a consumer could
trip over: an overlay expecting a datetime variable cannot have one.

### 018 — external event ingestion → **Not built**

> *hybrid registration model. Each source has a `strict` flag … or `discovery`
> flag (accepts unknown, quarantines them in an inspector UI for promotion to
> the registry).*

| Claim | Verdict | Evidence |
|---|---|---|
| Per-source `strict` flag | **Not built** | name: `grep -rn "strict" src/EventIngestion` → one hit, an unrelated comment about a grammar allow-list. job: no per-source validation mode on `IProvisionedFabSource` |
| Per-source `discovery` flag | **Not built** | name: no matches. job: no branch treating unknown event types differently |
| Quarantine of unknown events | **Partly — under another name** *(corrected)* | `src/EventIngestion/Domain/DeadLetter/DeadLetter.cs` captures rejected deliveries with topic, payload and error, documented *"Audit-only — no fan-out"*, listable via `ListDeadLettersQuery`. **This is quarantine for what *fails*; decision 018 is about what is *unknown*.** The first pass searched `quarantine`, `unregistered`, `unknownEvent`, `inspector` and `promote` — but not *dead letter*, the canonical alternative name |
| Inspector UI for promotion | **Not built** | No such page in `apps/management-web` |
| An event-type registry to promote into | **Not built** | `grep -ril "EventTypeRegistry\|RegisteredEventType" src/` → no matches |
| Validated events feed rules/overlays | **Holds** | Ingested events drive Automation rules and overlay variables |
| Quarantined events are audit-only | **Holds** *(corrected)* | Dead letters are explicitly audit-only, with no fan-out |

**The registration model is absent**, though not the whole of quarantine. There
is no registry to be unknown *relative to*, no per-source mode, and no promotion
path — an unrecognised type is ingested like any other. **Disposition**: correct
— issue 1972, whose premise was corrected once dead-lettering was found.

---

## Running tally (through 018)

| Range | Holds | Diverges | Not built | Unverifiable |
|---|---|---|---|---|
| 001–009 | 12 | 3 | 8 | 8 |
| 010–018 | 13 | 3 | 9 | 2 |


---

## Decisions 019–027 (T005)

### 019 — overlay expression language → **Diverges**

> *Overlay expression language = **CEL (Common Expression Language)** via a .NET
> implementation. … The same language is reused inside automation rule `where`
> clauses.*

| Claim | Verdict | Evidence |
|---|---|---|
| The language is CEL | **Diverges** | name: `grep -rl "Cel\b\|CelExpression" src/ --include=*.cs` → no matches. job: **the language exists and is hand-written** — `src/Automation/Application/Ael/` holds `AelLexer.cs`, `AelParser.cs`, `AelInterpreter.cs`, `AelExpression.cs`, `AelToken.cs` |
| Via a .NET CEL implementation | **Not built** | No CEL package referenced anywhere |
| Pure, typed, sandboxed, deterministic, no I/O | **Holds** | AEL is an expression interpreter with no I/O surface |
| Reused inside automation rule `where` clauses | **Holds** | The same AEL interpreter serves both |

**The job is done; the name is wrong.** This is the cleanest *diverges* in the
audit: a working language under a different name from the one decided.
**Disposition**: legitimise — ADR. *(Recording that AEL exists is not endorsing
it over CEL. If anyone thinks CEL was right, that is an issue.)*

### 020 — automation engine → **Holds (partly), interface not built**

| Claim | Verdict | Evidence |
|---|---|---|
| Declarative rules with trigger + action schema | **Holds** | `src/Automation/Domain/Rule/` |
| CEL conditions | **Diverges** | AEL — see 019 |
| Cooldowns, priority/conflict policy | **Holds** | Present in the rule aggregate |
| Builder UI | **Holds** | `apps/management-web` rules pages |
| **`IRuleEngine` as a strategy interface** | **Not built** | name: `grep -ril "IRuleEngine" src/` → no matches *(scoped to `src/`: this feature's own guard test names the symbol, so the original `tests/` scope no longer reproduces)*. job: `grep -ril "RuleEngine\|IRuleStrategy\|EngineTag"` → no matches. **§IX mandates this "in v1"** |
| Rule definitions engine-tagged | **Not built** | No engine tag on a rule |
| Execution issues commands via RabbitMQ, never direct DB writes | **Holds** | Rule actions publish; `Architecture.Tests` enforces the boundary |

**Disposition**: correct — issue. A §IX-mandated v1 interface is absent.

### 021 — event time reference → **Amended (spec 046, in progress)**

The `used_ts` / `time_basis` half is a separate claim from the frame-matching
half. Spec 046 is amending the frame-matching clause; the ingestion-time
behaviour is **not re-audited here**, to avoid two records of one thing.

### 022 — draft → preview → publish → **Holds (partly)**

| Claim | Verdict | Evidence |
|---|---|---|
| Applies to Overlays | **Holds** | Revision states in `OverlayDesigner` |
| Applies to Layouts, with assignment guard + render-error fallback | **Holds** | `LayoutComposition` revisions; the kiosk falls back on an archived overlay |
| Applies to Automation rules, with dry-run | **Holds** | `DryRunRuleRequest.cs`, `RulesEndpoints.cs` |
| Applies to Camera Catalog — staged config applied together | **Not built** | name: `grep -ril "staged\|pendingConfig\|applyTogether" src/CameraCatalog` → no matches. job: camera edits apply immediately, one field at a time |
| All other contexts edit live with an audit log | **Holds** | Audit events recorded per mutation |

**Disposition**: correct — issue, for the Camera Catalog clause only.

### 023 — authorization → **Diverges**

> *fixed RBAC for v1 with **4 roles (admin, operator, viewer, kiosk)** and
> per-context scope bundles … All authz checks go through
> `IAuthorizationDecisionPoint`.*

| Claim | Verdict | Evidence |
|---|---|---|
| Four realm roles: admin, operator, viewer, kiosk | **Diverges** | The realm defines **two**: `{"name": "user"}` and `{"name": "admin"}`. There is no `operator`, `viewer` or `kiosk` **role** — `operator` exists only as a *username* |
| Per-context scope bundles | **Holds** | `sse.management`, and per-endpoint `sse.*.write` scopes |
| Roles mapped to Keycloak realm roles | **Partly** | Two are; the other two do not exist |
| Customer-specific variants via groups | **Holds** *(differently)* | Groups exist and carry **fab** membership, not role variants |
| **`IAuthorizationDecisionPoint`** | **Not built** | name: `grep -ril "IAuthorizationDecisionPoint" src/` → no matches *(scoped to `src/`: this feature’s own guard test names the symbol, so the original `tests/` scope no longer reproduces)*. job: authorization is enforced by scope checks at endpoints. **§IX mandates this "in v1"** |

**Authorization works — by scopes and fab groups, not by the four named roles.**
**Disposition**: both. Legitimise the scope-based model by ADR; raise an issue
for the missing decision point.

### 024 — Aspire → **Holds**

| Claim | Verdict | Evidence |
|---|---|---|
| Aspire as orchestration/composition layer | **Holds** | `src/AppHost/AppHost.cs` declares every resource |
| Aspire dashboard for dev telemetry | **Holds** | The sink named by ADR-0118 |
| Production via `aspire publish` → Helm | **Not built** | See 025 |
| Aspire integrations preferred over ad-hoc config | **Holds** | Consistent throughout `AppHost.cs` |

### 025 — production orchestration → **Not built (partly)**

| Claim | Verdict | Evidence |
|---|---|---|
| Helm charts generated by Aspire's Kubernetes publisher | **Not built** | name: `grep -ril "PublishAsKubernetes\|AddKubernetes" src/AppHost` → only transitive `Aspire.Hosting.dll` binaries, no source. job: `deploy/helm/` contains **one** chart, for Mosquitto, hand-written |
| Per-fab 3-node k3s control plane, GPU-labelled workers | **Unverifiable here** | Deployment topology |
| Pilot also on k3s | **Unverifiable here** | Deployment |
| v2 Argo CD / Flux | **Unverifiable here** | v2 intent |

**Disposition**: correct — issue. Related to the open gateway-edge-wiring work.

### 026 — observability → **Amended (ADR-0118)**

Already corrected in the row. **But its consequences were not propagated** — see
009's Prometheus finding, and §IX below.

### 027 — repository layout → **Holds**

| Claim | Verdict | Evidence |
|---|---|---|
| Single monorepo, single solution | **Holds** | `SmartSentinelEye.slnx` |
| `src/` one project per context, Domain/Application/Infrastructure/Api | **Holds** | Nine contexts in that shape |
| `AppHost`, `ServiceDefaults`, `Shared.Kernel`, `Shared.Contracts` | **Holds** | All present |
| `apps/web/` React | **Diverges** *(harmlessly)* | Two apps at `apps/kiosk-web` and `apps/management-web`, not one `apps/web/` — split by ADR-0074 |
| `tests/`, `deploy/helm/`, `specs/`, `docs/adr/` | **Holds** | All present |
| Cross-context boundaries enforced by NetArchTest | **Holds** | `tests/Architecture.Tests/BoundaryTests.cs` |

---

## Constitution §IX (T006)

**Checked against accepted ADRs as well as code**, because that is how its
observability row went stale: no code changed, an ADR did.

| §IX row | Says | Verdict | Evidence |
|---|---|---|---|
| Rule engine | *Declarative + CEL (ADR-020)* → v2 visual workflow | **Diverges + not built** | The language is AEL, not CEL (019); and the **strategy interface §IX exists to mandate is absent** (020) |
| Authorization | *Fixed RBAC (ADR-023)* → v2 ABAC via OPA/Cedar | **Diverges + not built** | Two roles, not four; `IAuthorizationDecisionPoint` absent (023) |
| Camera adapter | *RTSP + ONVIF (ADR-005)* → v2 vendor SDKs | **Partly** | RTSP holds; **ONVIF is absent**, and the adapter seam the v2 column depends on does not exist |
| Observability sink | *Both Aspire + Grafana* → single chosen sink | **Contradicted by an accepted ADR** | **ADR-0118 abandoned the comparison** and chose the Aspire dashboard. §VII was updated by that ADR; §IX was not |

**§IX's purpose is defeated in three of four rows.** The section exists so v2 can
land without breaking changes — and two of the interfaces it mandates *"in v1"*
do not exist, while a third depends on an adapter seam that does not either.

**Disposition**: the observability row → legitimise (ADR-0118 already decided
it). The two missing interfaces → correct, issues.

---

## Final tally — recounted

> **The first published tally was wrong.** It read *"99 claims, 46 hold"*, and
> neither number reconciled with the tables above. Found in code review, recounted
> mechanically from the tables themselves rather than re-estimated.

**89 claims** in the decision tables, plus **§IX's 4 rows**, audited separately
because they are checked against accepted ADRs as well as code.

| Verdict | Count |
|---|---|
| Holds | 52 |
| Not built | 14 |
| Unverifiable here | 12 |
| Diverges | 7 |
| Partly | 3 |
| n/a (superseded or amended elsewhere) | 1 |
| **Total** | **89** |

**A fifth label appears, and it is a shorthand rather than a fifth verdict.**
*Partly* marks a claim whose sub-parts genuinely differ and which was not worth
splitting further — decision 022's four contexts, for instance, where three hold
and one does not. Recording it honestly beats forcing it into one of the four,
but it is the seam where the taxonomy stopped being clean, and a later audit
should split rather than inherit it.

**§IX**: one row holds, three do not — two describing interfaces that do not
exist, one contradicted by an accepted ADR (ADR-0118).

**Fifty-two of eighty-nine hold.** The reconnaissance guessed *"at least nine of
twenty-seven"* decisions were wrong; the audit finds problems in **fourteen**
decisions plus three §IX rows, and confirms the majority of claims are accurate.
Both halves matter: the record is worse than the spot-check suggested, and it is
not worthless.

### Three verdicts were wrong on the first pass

Found in code review, corrected above, and left visible rather than quietly
amended — an audit that hides its own misses is worth less than one that shows
them.

| Claim | First verdict | Corrected | How it was missed |
|---|---|---|---|
| 008 — device-bound credential | Not built | **Built, unused by the app** | Read the realm's browser client; never searched the code for the enrolment handler |
| 009 — MinIO "future" object store | Holds | **Diverges** — objects are archived today | Read the decision; never looked for a writer |
| 018 — quarantine of unknown events | Not built | **Partly, as dead-lettering** | Searched five synonyms, but not *dead letter* — the canonical one |

**All three are the same failure**: the second search this audit's own method
requires — for the *job*, under any other name — was not run. The rule was
written down and then not followed, which is worth more as a recorded example
than as an embarrassment quietly fixed.

---

## Dispositions (T007, T008, T009)

**Every non-holding claim lands in exactly one place** — an ADR that makes the
divergence legitimate, or an issue that proposes correcting it. **Nothing is left
as prose.** A note that does neither is how the situation being audited arose.

*Issue numbers are written without a `#` — this repo's automation closes a
merely-mentioned issue on merge, and every one of these must stay open.*

### Correct — issues raised

| # | Covers | Decisions |
|---|---|---|
| 1970 | §IX's two mandated v1 strategy interfaces are absent | 020, 023, §IX rows 1–2 |
| 1971 | Three of six declared variable types do not exist | 017 |
| 1972 | The hybrid event-registration model is entirely unbuilt | 018 |
| 1973 | ONVIF decided "on day one" is absent, as is the adapter seam | 005, §IX row 3 |
| 1974 | SFU sharding and coordinator unbuilt; single SFU unmarked as a SPOF | 012 |
| 1975 | Camera Catalog has no staged-config workflow | 022 |

**Tracked against existing work rather than duplicated**: decision 025's
ungenerated Helm charts are recorded as a comment on issue 1015, which is the
same deployment gap seen from the other end. Filing a near-duplicate would have
split the work.

### Legitimise — for the ADR (T009)

Each of these is a case where **the system is defensible and the decision is
stale**. Recording them is not endorsing them.

| Claim | Why legitimise rather than correct |
|---|---|
| **019 — AEL, not CEL** | A complete, working expression language exists. Nothing is missing; the decided *name* is wrong. If anyone believes CEL was the right choice, that is an issue against a working system — not a verdict this audit gets to make. |
| **009 + §IX + three constitution claims — Prometheus** | **ADR-0118 already decided this.** It abandoned the Grafana/Prometheus stack and chose the Aspire dashboard as the single sink. Nothing needs building; the record simply never followed an accepted ADR. |
| **008 — kiosk uses the authorization-code flow** | The kiosk authenticates safely as a public client with a view-only scope. The decision describes a device-bound `client_credentials` design that was not taken, and no defect follows from not taking it. |
| **023 — authorization by scopes and fab groups** | Authorization works and is enforced at every endpoint. Only the *four named roles* and the decision point diverge — and the decision point is issue 1970, so what remains here is the role model. |
| **027 — two web apps, not one `apps/web/`** | Split deliberately by ADR-0074. The layout decision predates it and was never updated. |
| **017 — `integer` and `decimal` merged into `Number`** | Recorded in both places on purpose: legitimising the *merge* is plausible, while the two **absent** types are issue 1971. The ADR should say which half it is legitimising. |

### Neither — and why that is not evasion

| Claim | Treatment |
|---|---|
| **011 — GPU transcode** | *"Only when forced"* has never been forced. An unrealised conditional intention, not a divergence. No issue: there is nothing to correct until an incompatible camera exists. |
| **009 — Marten** | *"Only where a context's invariants justify it"* is honestly satisfied by "not yet justified anywhere". Recorded as unrealised. **But CLAUDE.md states it more strongly than the decision does**, and that overclaim *is* corrected (T013). |
| **014, 021** | Already amended by specs 045 and 046. Recorded as amended rather than re-audited, so there is one record of each and not two. |
| **All 14 "unverifiable here" claims** | Deployment, hardware and v2-intent statements. **Refusing to guess is the verdict**, and it needs no follow-up — but the ADR must say the audit deliberately stopped rather than ran out. |

---

## Verification (T017, T018)

**Date**: 2026-08-29

### T017 — the full backend suite, as CI runs it

All **28 gated projects**, Release, **1 853 tests, zero failures** —
`Architecture.Tests` now 75 (was 64).

Run in full rather than as a subset **because spec 045 shipped a green subset
and CI caught an architecture test that had never been run locally**. The same
mistake here would have been especially poor, given this feature guards a
document with an architecture test.

### The guard was made to fail before it was trusted

A guard nobody has seen fail is worth nothing — the lesson spec 045's code
review delivered, where five tests passed against a component that never ran.

| Mutation | Result |
|---|---|
| Restore *"**Prometheus** for metrics"* to §Stack | **Failed** 1 of 75 |
| Change decision 019 back to *"The language is CEL"* | **Failed** 1 of 75 |
| Both reverted | 75 pass |

**The guard is a consistency check, not a text pin.** Each assertion reads the
code *and* the record and fails when they disagree — in either direction. So
building `IRuleEngine` does not fail the suite; building it and leaving §IX
recording it as absent does.

That is FR-012 satisfied by construction rather than by promise: **the guard
cannot obstruct progress, because progress is one of the two states it accepts.**
A guard that failed on legitimate work would be deleted within a month, taking
the corrections' protection with it.

### T018 — a person re-checks the boring rows

**Boring rows deliberately**, per [quickstart.md](./quickstart.md) §2. The
question is whether the *passes* were really performed, not whether the
discoveries were interesting — and the failures are the part nobody is tempted
to fake.

| Row | Recorded | Re-check | Reproduced |
|---|---|---|---|
| 001 | Holds — `IValueObject` present | file present | ✅ |
| 002 | Holds — no recording feature | second search on `IRecordingService`, `RecordingSegment`, `StartRecording` → none | ✅ |
| 009 | Holds — no EventStoreDB | no matches in `src/`, `tests/` | ✅ |
| 010 | Holds — RabbitMQ, 24 references | count is 24 | ✅ |
| 016 | Holds — nine bounded contexts | count is 9 | ✅ |
| 022 | Holds — rule dry-run | `DryRunRuleRequest.cs` present | ✅ |
| 027 | Holds — layout claims | `.slnx`, `Shared.Kernel`, `Shared.Contracts`, `tests/`, `docs/adr/` all present | ✅ |

**Seven of seven reproduced.** None was recorded on an assertion alone; each had
a command, and each command still returns what the audit says.

### What this verification does not establish

- **That every one of the 52 holding claims was checked as carefully.** Seven
  were re-run. The rest rest on the auditor's discipline, and no test can
  distinguish a thorough audit from a plausible one — the artefact of both is
  prose asserting that someone looked.
- **That the 14 "unverifiable here" claims are true.** They are unverifiable;
  that is the verdict. The fab's VLAN topology may be exactly as decision 013
  says, or not.
- **That the other ~100 ADRs are accurate.** Out of scope, and ADR-0117's leg
  table and ADR-0026's abandoned stack suggest the same drift lives there.
- **That the audit stays true.** It is accurate on 2026-08-29 and starts decaying
  immediately. The guard slows that for the corrected claims and does nothing for
  the other 46.
