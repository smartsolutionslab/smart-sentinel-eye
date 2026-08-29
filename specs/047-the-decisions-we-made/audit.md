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
| Device-bound credential | **Not built** | name: no device-code or device-binding config in the realm. job: the kiosk signs in interactively — spec 045's contract test had to drive a browser session precisely because no token can be minted for it out of band |
| View-only scope | **Holds** | Kiosk token carries read scopes only; asserted in `e2e/kiosk-identity.spec.ts` |
| Operators use auth-code flow and bind to a kiosk | **Partly** — auth-code holds; **binding not built** | `smart-sentinel-eye-web` uses the code flow; no kiosk-binding mechanism exists |
| No PTZ without an operator token | **Unverifiable here** | PTZ is not built at all, so the constraint has nothing to bind |

**Disposition**: legitimise — ADR. The system's choice is defensible; the row
describes a design that was not taken.

### 009 — persistence and infrastructure → **Mixed, and one systemic finding**

| Claim | Verdict | Evidence |
|---|---|---|
| PostgreSQL as the default | **Holds** | `timescale/timescaledb` container; every context persists to Postgres |
| Marten where invariants justify it | **Not built** *(unrealised intention)* | name: `grep -ril "marten" --include=*.csproj` → no package reference; the only source hit is a comment in `Camera.cs`. job: no event sourcing anywhere. **"Not yet justified anywhere" is a fair reading** — recorded as unrealised, not false |
| **Prometheus for metrics** | **Not built — and contradicted by an accepted ADR** | name: only occurrence in `src/` is a *comment* in `mediamtx.yml` about MediaMTX's own exposition format. job: no Prometheus container in `AppHost.cs`, no exporter package. **ADR-0118 abandoned the Grafana/Prometheus stack and chose the Aspire dashboard as the single sink** |
| MinIO as the future object store | **Holds** | Declared in `AppHost.cs`; "future" is accurate — nothing stores objects yet |
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
| Quarantine of unknown events | **Not built** | name: `grep -ril "quarantine" src/ apps/` → no matches. job: `grep -ril "unregistered\|unknownEvent\|inspector\|promote"` → no matches |
| Inspector UI for promotion | **Not built** | No such page in `apps/management-web` |
| An event-type registry to promote into | **Not built** | `grep -ril "EventTypeRegistry\|RegisteredEventType" src/` → no matches |
| Validated events feed rules/overlays | **Holds** | Ingested events drive Automation rules and overlay variables |
| Quarantined events are audit-only | **n/a** | Nothing is quarantined |

**The whole registration model is absent**, not merely its UI. Both searches ran
and both failed. **Disposition**: correct — issue.

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
| **`IRuleEngine` as a strategy interface** | **Not built** | name: `grep -ril "IRuleEngine" src/ tests/` → no matches. job: `grep -ril "RuleEngine\|IRuleStrategy\|EngineTag"` → no matches. **§IX mandates this "in v1"** |
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
| **`IAuthorizationDecisionPoint`** | **Not built** | name: `grep -ril "IAuthorizationDecisionPoint\|DecisionPoint" src/ tests/` → no matches. job: authorization is enforced by scope checks at endpoints. **§IX mandates this "in v1"** |

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

## Final tally

| Range | Holds | Diverges | Not built | Unverifiable |
|---|---|---|---|---|
| 001–009 | 12 | 3 | 8 | 8 |
| 010–018 | 13 | 3 | 9 | 2 |
| 019–027 | 20 | 4 | 6 | 4 |
| §IX | 1 | 3 | 3 | 0 |
| **Total** | **46** | **13** | **26** | **14** |

**99 claims across 27 decisions and 4 §IX rows. Forty-six hold.**

**The reconnaissance said "at least nine of twenty-seven".** The audit finds
problems in **fourteen** decisions plus three §IX rows — and confirms thirteen
decisions substantially hold. Both halves matter: the record is worse than the
spot-check suggested, and it is not worthless.
