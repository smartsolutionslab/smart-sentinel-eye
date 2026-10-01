# Spec 296 — The rule that turns the wall

**Issue:** [#2618](https://github.com/smartsolutionslab/smart-sentinel-eye/issues/2618)
— "An Automation rule switches a wall (US2, follow-up to #2608)".
**Branch:** `feat/2618-automation-switches-a-wall` (cut from `origin/develop` @ `6f0f0f12`)
**Created:** 2026-09-30
**Lane:** **supervised** (corrected 2026-10-01). Drafted as autonomous; the user has since
confirmed ADR-0157's exclusion applies and #2618 carries `agent:blocked`, not
`agent:ready`. All seven ADR-0037 gates apply. See §8 R-lane.
**Re-verified:** 2026-10-01 against `develop@a7b48a96` — no commit touched `src/Automation`,
`src/LayoutComposition`, `src/Shared.Contracts`, `src/AuditObservability` or the WolverineFx
pin (6.40.0) since `6f0f0f12`; §1's ground truth holds. Frontend drift is recorded in
plan §4.2.

**Parent:** spec 263 (`specs/263-a-wall-that-changes-its-scene/`), whose §4 "User Story 2"
is this spec's starting point. That section was written *before* US1 existed. This spec
re-derives it against what US1 actually shipped in PR #2617, and changes three of its
statements (§1.9–§1.11). Where this spec and spec 263's US2 section disagree, this spec
governs.

**ADRs and specs this spec is bound by (it applies them; it decides nothing they left open):**

- **ADR-0157** — event-driven scene rotation. §2: switching is a new `RuleAction` kind on
  Automation's existing engine. This spec builds that kind.
- **ADR-0158** — a scene references a Layout chain, not a Revision. The rule's target is a
  `LayoutIdentifier`.
- **Spec 263 PD-1** (product owner, 2026-09-26) — **last writer wins**; no precedence, no
  hold. **PD-4** (manual switch = management-web + `sse.layouts.write`), **PD-6** (skip /
  refuse unpublished scenes) also bind.
- **ADR-0099** — AEL. `$.payload.line == 3` is an ordinary AEL predicate today (§1.4). No
  new filter mechanism.
- **ADR-0113** — two-layer optimistic concurrency; **"automatic retry is forbidden in both
  the backend and the SPAs."** This is load-bearing for FR-011 (§1.10).
- **ADR-0040/0073** — separate domain and `V<N>` integration events.
- **ADR-0088/0126** — Postgres outbox + durable inbox; LayoutComposition keeps the durable
  inbox.
- **Spec 007 FR-018** — the rule-effect dedup precedent (`IVariableValueRequestDedupStore`).
- **Spec 013 / #1252** — fab scoping of rules and their effects; a fab-scoping miss and a
  typo must not look identical in the log (the `VariableNotInFab` precedent).
- Conventions relied on without question: constitution §II (no primitives on domain
  models, ADR-0139/0140), §III (no cross-context references), ADR-0038/0046/0066 (VOs),
  ADR-0039/0090 (`…Identifier`), ADR-0047/0089 (`Result`/`ApiError`), ADR-0105
  (`Ensure.That`), ADR-0141 (`Option<T>`), ADR-0092/0093 (folder layout), ADR-0103
  (Aspire fixture), ADR-0139 (red first), ADR-0162 (e2e helpers per surface).

**No new ADR is needed** — *provided FR-011 is built as written*. §1.10 records why the
alternative spec 263's plan sketched (retry the rule switch on a concurrency conflict)
would contradict ADR-0113's text and therefore need an amendment this lane may not write.

**Latency budget (constitution §IV).** A scene switch is **not** on the event→overlay path
(spec 263 header, ADR-0157 §Consequences) and spends no leg. One code path this spec edits
*is* on a leg: Automation's `FabEventIngestedV1Handler` fan-out loop sits inside **Event →
overlay state (≤ 200 ms)** for the *other* effects of the same event. The change adds one
in-memory publish capture per matched `SwitchWallScene` rule and no I/O. Expected impact:
none measurable. Obligation: the existing `AcceptToDecideLatencyTests` stay green unmodified
(tasks T125).

**Phase-4a colour: red.** Everything here is new behaviour. There is no refactor.

---

## 1. Ground truth (verified against `origin/develop` @ `6f0f0f12`, 2026-09-30)

### What US1 (PR #2617) shipped that US2 builds on

1. **The domain was already built for US2.** `SceneSwitchCause.Rule(RuleIdentifier By,
   CausingEventIdentifier CausingEvent)` exists
   (`src/LayoutComposition/Domain/Wall/SceneSwitchCause.cs`), as do LayoutComposition's
   context-local `RuleIdentifier` and `CausingEventIdentifier`. `Wall.SwitchTo(SceneTarget,
   IReadOnlySet<LayoutIdentifier> publishable, SceneSwitchCause, IClock)` accepts it.
   **No `Wall` aggregate change is needed.**
2. **`WallSceneChangedV1` already carries the rule shape** (`Cause: "Rule"`, `Rule`,
   `CausingEventIdentifier`, `Metadata.Actor = null`) and
   `WallSceneSwitchedDomainEventHandler.Describe` already maps `SceneSwitchCause.Rule` to
   it. It is already audited (`IntegrationEventAuditHandler`, `V1ResourceMap`).
3. **The broadcast is post-commit — the issue's first callout is accurate.** The domain
   event handler publishes `WallSceneChangedV1` only; the SignalR frame is sent by
   `WallSceneChangedV1Handler`, a Wolverine subscriber on that event, which runs only once
   the outbox releases it. A rule-driven switch therefore reaches the kiosk through exactly
   the same path as a manual one. **Kiosk-web needs no change.** Management-web's
   `WallDetailPage` invalidates its cached wall on every `WallSceneChanged` frame, so it
   also follows rule switches with no change, and its next manual switch carries the fresh
   version.
4. **No gateway route is needed — the issue's second callout is accurate, and goes
   further.** Walls are served through the existing `/layout-composition/{**catch-all}`
   route (`walls.api.ts` uses `layout-composition/walls`). US2 adds **no HTTP route at
   all**: rules are created through the existing `/automation/{**catch-all}` →
   `POST /rules`, which only gains body fields. Automation → LayoutComposition is a
   RabbitMQ message, not HTTP. **No `infra-engineer` work.**
5. **The manual path's shape is the template for the rule path**, minus `If-Match`:
   `SwitchWallSceneCommandHandler` checks not-in-set (→ 400) and already-showing (no-op)
   *before* the publishability lookup, then not-published (→ 409), then calls `SwitchTo`.
   The rule path makes the same checks in the same order and turns each refusal into a
   logged drop (FR-010).
6. **`IWallRepository.FindAsync(wall, fabs, ct)` puts the fab in the lookup.** Passing
   `[Metadata.Fab]` makes a wall in another fab indistinguishable from a missing one at the
   *lookup*, which is correct for the write. It is not enough for the *log* (FR-010, #1252).

### Automation today

7. **`RuleAction` has two variants** (`SetVariableValue`, `HighlightOverlay`), packed into
   `action_packed` (**`text`, unbounded** — no migration for a longer form; spec 263's
   "check at 4b" is settled). `RuleEvaluator` switches on the variant, emitting a
   `RuleActionEffect`; `FabEventIngestedV1Handler` switches on the effect and publishes one
   V1 per effect with `Metadata = (new id, requestedAt, fab, Actor: null,
   RootIngestedAt: ingestedAt)`. **Effects carry no rule identifier**;
   `CompiledRule.Identifier` is available at evaluation time.
8. **The rule API validates action shape at the edge.** `RulesEndpoints.BuildAction`
   throws `ArgumentException` for a missing field; the endpoint maps it to
   `400 RULE_INVALID_INPUT`. **That is "the existing typed rule-validation error" US2-7
   refers to.** `CreateRuleCommandHandler` needs no change (it only parses AEL).
   `DryRunRuleQueryHandler` is already generic: an action with no value expression reports
   `Matched: true, EvaluatedValue: null`. **It needs no change either** — only a test.
9. **Event filtering needs nothing new.** AEL (ADR-0099) already evaluates
   `$.payload.line == 3` against `{source, kind, device, payload}`. The issue's
   "JSONPath-style filter" is the existing predicate language, not a new pattern.

### Where spec 263's US2 section was wrong or incomplete

10. **Retrying the rule switch on a concurrency conflict would contradict ADR-0113.** Spec
    263 plan §3 proposed letting Wolverine re-deliver on `DbUpdateConcurrencyException`
    and argued this "is not retry-on-conflict in ADR-0113's sense". ADR-0113's corrections
    section says, without qualification, that **"automatic retry is forbidden in both the
    backend and the SPAs"**, and names re-applying a mutation to freshly-loaded state as
    the defect. Reading the rule path out of that sentence is the same move PD-3 made
    against ADR-0157 §1, and PD-3 needed ADR-0158 to make it legitimate. This lane may not
    write that amendment. **Decision within existing ADRs:** a rule switch that loses a
    concurrency race is **not retried** — it is dead-lettered and nothing is announced
    (FR-011). US2-6 is restated accordingly. Additionally, **no Wolverine error policy
    exists anywhere in the repo today** (`grep OnException|RetryWith|MaximumAttempts` →
    nothing), so behaviour on the pinned WolverineFx 6.40.0 default is unverified; FR-011
    pins it explicitly instead of inheriting it.
11. **A conflict that is caught and swallowed would announce a switch that never
    happened.** Inside a Wolverine handler, `OutboxEventBus` publishes to the *ambient*
    message context, which Wolverine flushes when the handler returns successfully. The
    domain-event dispatch in `WallRepository.SaveAsync` runs *before* the commit. So if the
    handler caught the conflict and returned, `WallSceneChangedV1` — already captured — would
    be flushed for a write that rolled back. That is US1's phase-6 finding (pre-commit
    broadcast) in a new form. FR-011 therefore requires the exception to **escape** the
    handler, so Wolverine discards the captured messages.
12. **"The durable inbox absorbs duplicates" covers one hop, not the one that matters for
    `Next`.** Spec 263 R5 and plan §3 relied on LayoutComposition's durable inbox and said
    *not* to add an application-level dedup table. The inbox deduplicates a redelivered
    *envelope*. It cannot recognise a second `WallSceneSwitchRequestedV1` minted by an
    upstream re-run (a fresh `Metadata.EventIdentifier` per publish,
    `FabEventIngestedV1Handler`). `SetVariableValue` is idempotent in value and still got a
    dedup store (spec 007 FR-018) for this reason; `Next` is the **first non-idempotent
    rule action** (the existing redelivery test says so: "rules do not currently have an
    action that increments"). FR-012 adds the store, mirroring the precedent.
13. **FR-010 omitted a drop case.** A `Layout` target that is **not in the wall's scene set**
    — a rule authored before the wall was edited, or a typo (R3) — hits `Wall.SwitchTo`'s
    programmer-error throw unless the handler pre-checks it. Added to FR-010.
14. **US2-8 and US2-9 must not log identically.** Spec 263 said a cross-fab rule and an
    unknown wall log "the same". The SystemVariables handler learned the opposite from
    #1252: its `VariableNotInFab` message exists because a fab-scoping bug and a typo looked
    identical in the log for a release. FR-010 distinguishes them.

---

## 2. Scope

**In scope:** US2 (P1 for this spec) — an Automation rule with a `SwitchWallScene` action
switches a wall when a matching event is ingested, attributed to the rule and the event.
Delivered in two PRs (plan §9): **PR-A** backend (authorable over the API, observable end
to end with no UI), **PR-B** management-web rule editor.

**Out of scope** (unchanged from spec 263 §3): scheduled/time-based rotation (reopens
PD-1; its own spec), archiving a wall, kiosk-side switching, keeping WebRTC sessions across
a switch, inter-display sync. Also out of scope here: retrying a rule switch that lost a
concurrency race (§8 R-retry), a TTL sweeper for the dedup table (§8 R-ttl), validating the
wall at rule-creation time (R3).

---

## 3. User stories

### User Story 1 — A rule switches a wall (P1, PR-A)

A fab admin authors a rule: *when a `line-stop` event arrives with `$.payload.line == 3`,
switch wall "Line 3 rotation" to layout "Line 3 fault view"*. They publish it. When a
matching event is ingested, the wall switches, every kiosk showing the wall follows, and the
audit trail names the rule and the triggering event.

**Why P1:** it is the whole feature on the wire. Everything the UI adds in US2 is
authoring convenience over an API that already works.

**Independent test (PR-A, no UI):**

1. `aspire run`. As admin, create two Published layouts A and B and a wall W with scenes
   `[A, B]` (US1 flow). W shows A. Open a kiosk on W.
2. `POST /automation/rules` `{ name: "line-3-fault", triggerSource: <the manual endpoint's
   source>, triggerKind: "line-stop", predicate: "$.payload.line == 3",
   actionType: "SwitchWallScene", wallIdentifier: W, sceneTarget: "Layout",
   targetLayoutIdentifier: B }` → 201. `GET` it: the action round-trips. Publish it.
3. `POST /event-ingestion/events/manual` `{ kind: "line-stop", payload: { line: 3 } }`.
   Within ~1 s the kiosk renders B; `GET /layout-composition/walls/W` shows B,
   `sceneVersion` +1.
4. Audit: one `WallSceneSwitchRequestedV1` (resource = W) and one `WallSceneChangedV1`
   with `Cause: "Rule"`, `Rule` = the rule, `CausingEventIdentifier` = the event,
   `Metadata.Actor` null.
5. PD-1: switch W back to A by hand (US1). Ingest another matching event. W shows B again.
   Ingest `{ line: 4 }`: nothing changes.
6. The Aspire trace view shows one trace spanning ingest → Automation → LayoutComposition
   → hub.

### User Story 2 — An admin authors a wall-switch rule in management-web (P2, PR-B)

In the rule dialog the admin picks **Action: Switch a wall's scene**, picks the wall from a
list of their walls, then picks **Next scene** or one of that wall's scenes (by layout
name). Saving creates the same rule US1's API call does. The rules list describes the
action in words.

**Independent test (PR-B):** repeat US1's steps 2–3 through the rule dialog instead of
`POST /rules`; the kiosk switches. The rules list shows "Switch *Line 3 rotation* to *Line
3 fault view*" (or "… to its next scene").

---

## 4. Acceptance scenarios (Gherkin)

```gherkin
Scenario: US2-1 Create a rule with a SwitchWallScene action (happy path)
  Given an admin with sse.rules.write in fab F
  When the admin POSTs /rules { actionType: "SwitchWallScene", wallIdentifier: W,
       sceneTarget: "Layout", targetLayoutIdentifier: B, ... }
  Then the response is 201
    And GET /rules/{name} returns action { kind: "SwitchWallScene", wall: W,
        sceneTarget: "Layout", targetLayout: B } exactly
    And the same holds for sceneTarget "Next" with targetLayout null

Scenario: US2-2 A matching event switches the wall
  Given an Active rule R in fab F: SwitchWallScene(W, Layout B)
    And wall W in fab F shows A with scenes [A, B], both Published
  When a FabEventIngestedV1 e in fab F matches R
  Then WallSceneSwitchRequestedV1 { Wall: W, Target: "Layout", TargetLayout: B, Rule: R,
       CausingEventIdentifier: e } is published with Metadata.Fab = F, Actor = null
    And W shows B with sceneVersion + 1
    And WallSceneChangedV1 { Cause: "Rule", Rule: R, CausingEventIdentifier: e,
        Metadata.Actor: null } is published and audited
    And a WallSceneChanged frame reaches fab F's hub group

Scenario: US2-3 "Next" from a rule
  Given an Active rule with sceneTarget "Next" on wall W showing A of [A, B]
  When it fires
  Then W shows B

Scenario: US2-4 Manual, then rule — last writer wins (PD-1)
  Given rule R switched W to B, and then an admin switched W to A by hand
  When R fires again
  Then W shows B

Scenario: US2-5 Rule, then manual — last writer wins (PD-1)
  Given rule R switched W to B
  When the admin re-reads W and switches it to A with the current If-Match
  Then W shows A and stays on A until R next fires

Scenario: US2-6 A manual switch and a rule switch race (restated — see §1.10)
  Given a manual switch and a rule-driven switch of W are in flight at the same time
  Then exactly one of them commits first
    And if the rule commits first, the manual switch is refused 409 (*_STALE) and not applied
    And if the manual switch commits first, the rule switch is not applied, not retried,
        and is dead-lettered; no WallSceneChangedV1 is published for it
    And in every ordering: the number of WallSceneChangedV1 published for W equals W's
        sceneVersion delta, and W's Showing equals the CurrentLayout of the last one

Scenario: US2-7 Bad request — incomplete or contradictory action
  When the admin POSTs /rules with actionType "SwitchWallScene" and
    | case                                        |
    | no wallIdentifier                           |
    | sceneTarget missing or not Next/Layout      |
    | sceneTarget "Layout", no targetLayoutIdentifier |
    | sceneTarget "Next" with a targetLayoutIdentifier |
    | an empty GUID for wall or layout            |
  Then the response is 400 RULE_INVALID_INPUT and no rule is created

Scenario: US2-8 A rule in another fab cannot move the wall
  Given wall W in fab F and an Active rule in fab G naming W
  When the rule fires
  Then W is unchanged, no WallSceneChangedV1 is published
    And a warning is logged naming W, fab G, and fab F as the wall's actual fab

Scenario: US2-9 A rule naming an unknown wall
  When a rule targeting a wall identifier that exists in no fab fires
  Then nothing changes, no WallSceneChangedV1 is published
    And a warning is logged naming the wall identifier, distinct from US2-8's message

Scenario: US2-10 The same rule firing for the same event is applied at most once
  Given WallSceneSwitchRequestedV1 { Target: "Next", Rule: R, CausingEventIdentifier: e }
    for W has been applied
  When a second WallSceneSwitchRequestedV1 with the same Rule R and CausingEventIdentifier e
    arrives (a redelivery or an upstream re-run — a different envelope)
  Then W advances exactly once in total, and the duplicate is logged as a dedup hit
    But two *different* rules firing on the same event each apply (sceneVersion + 2)

Scenario: US2-11 Auth — rule creation needs sse.rules.write
  When a caller without sse.rules.write POSTs a SwitchWallScene rule
  Then the response is 403

Scenario: US2-12 The target is no longer in the wall's scene set (new — §1.13)
  Given an Active rule R: SwitchWallScene(W, Layout C), and W's scenes were edited to [A, B]
  When R fires
  Then W is unchanged, no WallSceneChangedV1 is published, and a warning names W and C

Scenario: US2-13 The target is not Published (PD-6)
  Given an Active rule R: SwitchWallScene(W, Layout B), and B has been archived
  When R fires
  Then W is unchanged, no WallSceneChangedV1 is published, and a warning names W and B
    And a "Next" rule skips B, and is a no-op if no other scene is Published

Scenario: US2-14 Already showing the target is a no-op
  Given W shows B
  When a rule targeting Layout B fires
  Then sceneVersion is unchanged and no WallSceneChangedV1 is published

Scenario: US2-15 Dry run of a SwitchWallScene rule
  When the admin dry-runs the rule against a matching sample
  Then the result is { matched: true, evaluatedValue: null } and nothing is published

Scenario: US2-16 (PR-B) The rule editor authors the action
  Given an admin in management-web with walls W1 and W2
  When they choose "Switch a wall's scene", pick W1, pick a scene or "Next scene", and save
  Then POST /rules is sent with the matching SwitchWallScene fields
    And leaving the wall or (for a named scene) the scene empty is a form error, not a request
    And switching the action type away and back does not submit stale fields
```

---

## 5. Functional requirements

- **FR-001** `RuleAction.SwitchWallScene(WallIdentifier Wall, SceneTarget Target)` is a
  third variant. `SceneTarget` is Automation's own `Next | Layout(LayoutIdentifier)`;
  `WallIdentifier` and `LayoutIdentifier` are Automation-local copies (§III, the
  `OverlayIdentifier` precedent). No primitives on the domain type (§II).
- **FR-002** It persists in `action_packed` as `SwitchWallScene|<wall>|Next` or
  `SwitchWallScene|<wall>|Layout|<layout>`, round-trips unchanged, and needs no migration.
- **FR-003** `POST /rules` accepts `wallIdentifier`, `sceneTarget` (`"Next"` | `"Layout"`,
  PascalCase like `actionType`), `targetLayoutIdentifier`. US2-7's cases are
  `400 RULE_INVALID_INPUT`. `RuleActionDto` gains `wall`, `sceneTarget`, `targetLayout`
  and a `SwitchWallScene` kind; exactly one variant's fields are populated.
- **FR-004** A matched `SwitchWallScene` rule produces one `WallSceneSwitchRequestedV1`
  per firing, carrying the rule identifier from `CompiledRule.Identifier`, the causing
  event identifier, and metadata built exactly as the highlight branch builds it
  (`Metadata.Fab` = the event's fab, `Actor` null, `RootIngestedAt` forwarded). Existing
  effects are **not** retro-fitted with a rule identifier.
- **FR-005** Dry-run reports `matched`, no value, and publishes nothing (US2-15).
- **FR-006** LayoutComposition applies a request as `SwitchTo(target, publishable,
  SceneSwitchCause.Rule(rule, causingEvent), clock)` against **whatever is current**, with
  no expected version (PD-1; spec 263 FR-005).
- **FR-007** `WallSceneChangedV1` for a rule switch has `Cause "Rule"`, `Rule`,
  `CausingEventIdentifier`, `Metadata.Actor` null — **already true of the shipped mapping;
  pinned by test, not rebuilt.**
- **FR-008** `WallSceneSwitchRequestedV1` is audited (`IntegrationEventAuditHandler` +
  `V1ResourceMap`, resource kind `Wall`, identifier = `Wall`) in the same commit that adds
  the contract, because two existing completeness tests fail otherwise.
- **FR-009** The kiosk and management-web follow a rule switch through the shipped
  `WallSceneChangedV1Handler` → hub path with **no change** (§1.3). Pinned end to end.
- **FR-010** LayoutComposition **drops** a request, publishes nothing, and logs a
  structured `[LoggerMessage]` warning naming the wall identifier the request targeted,
  the rule and the causing event, when:
  (a) `Metadata.Fab` is absent or unparseable;
  (b) the wall exists in **another** fab — the message also names both fabs;
  (c) the wall exists in **no** fab — a *different* message from (b);
  (d) a `Layout` target is not in the wall's scene set — names the layout;
  (e) a `Layout` target is not Published — names the layout (PD-6);
  (f) `Target`/`TargetLayout` violate the contract (unknown target, `Layout` without a
  layout) — a publisher bug, since Automation's domain cannot produce it.
  "Already showing" and "`Next` finds nothing else Published" are **no-ops**, logged at
  information level like the manual path's `WallSceneSwitchWasNoOp`, not warnings.
- **FR-011** A rule switch whose commit loses an optimistic-concurrency race is **not
  retried**. `DbUpdateConcurrencyException` escapes the handler (so Wolverine discards the
  captured `WallSceneChangedV1`, §1.11), and a failure rule scoped to
  `WallSceneSwitchRequestedV1` moves the envelope to the dead-letter store on that
  exception type **on the first attempt**. No other exception type's handling changes.
- **FR-012** A request is applied **at most once per (rule, causing event)**. Before
  applying, the handler reserves `(rule, causingEvent)` in a LayoutComposition dedup store
  (`INSERT … ON CONFLICT DO NOTHING`, the spec 007 FR-018 mechanism); a failed reservation
  is a logged dedup hit and a no-op. The rule is part of the key so that two rules firing
  on one event both apply (the #2214 lesson). Drops (a)–(c) and (f) happen **before** the
  reservation and consume no key.
- **FR-013** (PR-B) The rule editor offers the action with a wall select (the caller's
  walls, via the existing `walls.api`) and a target select (`Next scene` + that wall's
  scenes by layout name). The Zod schema enforces FR-003's shape client-side. The rules
  list describes the action in words.

## 6. Success criteria

- **SC-001** Every scenario in §4 is an automated test at the lowest layer that can prove
  it: domain/VO and converter tests for FR-001/002, Application tests for FR-004/005/006,
  FR-010, FR-012, US2-14; Aspire-fixture integration for US2-1, 2, 3, 4, 5, 7, 8, 11, and
  the audit rows; Playwright for US2-2 in PR-B. US2-6 and US2-10 have Application-level
  tests that are decisive, plus the integration invariant in US2-6.
- **SC-002** Coverage gates hold (ADR-0065): Domain ≥ 90 %, Application ≥ 80 %.
- **SC-003** `PrimitiveBoundaryTests`, `HandlerDeconstructionTests`, `BoundaryTests`
  (including `Every_integration_event_has_an_audit_handler`) and
  `V1ResourceMapTests` completeness pass with no new suppression or carve-out.
- **SC-004** `AcceptToDecideLatencyTests` and `EventReachesItsEffectsTests` pass
  unmodified.
- **SC-005** Phase 5 walks US1's independent test live, including step 5 (PD-1 observed)
  and step 6 (one trace), and records it in `verification.md`.

## 7. Assumptions (marked, not buried)

- **A1** Rule authors in management-web hold `sse.layouts.read` (the admin bundle does), so
  the rule editor can list walls. If the wall list fails to load, the select shows the error
  and the form cannot be submitted with a wall; no free-text fallback is built.
- **A2** Rules are immutable after creation (no edit endpoint exists); a rule's wall/target
  can go stale only through wall edits or layout archival — both covered by FR-010.
- **A3** WolverineFx 6.40.0 discards a handler's captured outgoing messages when the
  handler throws, and supports a failure rule scoped to one message type. **Not verified
  against the pinned version at Phase 2.** What the tests can and cannot prove: the
  Application test proves the handler lets the conflict escape; T114 asserts the failure
  rule is registered for this message type and no other (and must be shown failing by
  counterfactual — remove the rule, see it red); US2-6's integration invariant catches a
  phantom `WallSceneChangedV1` only in whatever race ordering a run happens to produce.
  A deterministic live provocation of the conflict is not attempted; if T114 proves
  impractical against Wolverine's API, the PR records the gap in words rather than
  claiming coverage.

## 8. Risks and open items

- **R3 (inherited, accepted)** Automation cannot check at rule-creation time that a wall
  exists or is in the rule's fab — the same as `HighlightOverlay`. Mitigated only by
  FR-010's distinct log lines.
- **R-retry** Under FR-011 a rule switch that loses a millisecond race with a manual switch
  is dead-lettered, not applied. Spec 263 plan §3 wanted it re-applied. Doing so means an
  automatic backend retry of a conflicting write, which ADR-0113 forbids in terms; it is a
  **follow-up needing an ADR-0113 amendment by a human**, not something this lane builds.
  The dead-lettered envelope and the audited-but-unmatched `WallSceneSwitchRequestedV1`
  make every such loss visible.
- **R-ttl** The dedup table grows by one row per rule firing that targets a wall. The
  SystemVariables table has the same property and its "7-day TTL cleanup worker" was never
  built. Not built here either (§IX); a follow-up issue if the table ever matters.
- **R-lane (resolved: supervised)** ADR-0157's implementation notes exclude its delivery
  from the autonomous lane "because it carries open design questions". This spec first
  argued PD-1..6's confirmation lifted that; the user confirmed on 2026-10-01 that the
  exclusion stands, so #2618 runs supervised with every gate. The only design point this
  spec settles (§1.10) it still settles *inside* ADR-0113 rather than against it; under
  the supervised lane a human may instead choose the ADR-0113 amendment (R-retry).
- **R5 (inherited, narrowed)** LayoutComposition must keep the durable inbox (no native
  acks). FR-012 no longer depends on it for `Next`'s safety, but the inbox still stops a
  redelivered envelope before the handler runs.
