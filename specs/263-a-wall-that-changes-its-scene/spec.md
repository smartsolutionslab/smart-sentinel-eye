# Spec 263 — A wall that changes its scene

**Issue:** [#2608](https://github.com/smartsolutionslab/smart-sentinel-eye/issues/2608)
— *Deliver ADR-0157: event-driven scene rotation for walls*. **Supervised lane only**:
ADR-0157 §Implementation Notes and the issue both exclude it from the autonomous lane
(ADR-0144), because it carries open design questions a human must settle.

**Status:** Phase 1-3 (Specify/Plan/Tasks) drafted. **Decisions PD-1 through PD-6 were
confirmed directly by the product owner (Heiko) on 2026-09-26**, all matching this spec's
own proposed answers. Past the gate for **US1**. **US2 is parked** — deliver US1 first,
as a separate PR; US2 is its own follow-up once a scheduled-trigger spec exists (§3, R2)
and reopens PD-1.

**Marker convention.** `[NEEDS PRODUCT DECISION: PD-n]` is different from spec-kit's
`[NEEDS CLARIFICATION]`. It means that the author has proposed an answer and a human
must confirm it. It does not mean the author has no answer. This spec does not use the
spec-kit marker anywhere as an open item.

**Spec number.** Originally drafted as spec 258, checked 2026-09-26 against `origin/develop`
(then at 257). **Re-checked before opening the PR, same day**: `develop` had since advanced
to `specs/261-the-face-the-fab-cannot-fetch`, and PR #2611 had independently claimed
`specs/258-the-bundle-the-hook-still-honours` (a different feature) in the meantime — the
exact collision this note warned about. **Renumbered 258 → 263** (past #2616's
`specs/262-the-tile-that-claims-a-rectangle`, the highest number claimed anywhere at
re-check time) before the PR was opened. If a future rebase reveals another collision at
263, re-check again — this is a live race, not a one-time fact.

**ADRs referenced:**

- **ADR-0157** (event-driven scene rotation) is the decision this spec delivers. **Its PR
  (#2606) is still open**: the ADR file in this worktree is an untracked copy, identical
  to #2606's version. See Risk R1.
- **ADR-0112** (multi-tile layouts) is the `Layout`/`Revision` aggregate, at most one
  Published revision per chain, and the `/hubs/layouts` push precedent. **ADR-0156**
  (3×3 walls, also in #2606) sets how many tiles a scene switch re-negotiates. That is
  4 today, and 9 once ADR-0156 lands.
- **Spec 007 / 013** cover the Automation `Rule` aggregate and its fab-scoping. **Spec 017
  / 067** cover layout fab-scoping and a wall showing only one fab.
- Conventions this spec relies on, and does not question:
  - value objects and no primitives on domain models: constitution §II, ADR-0038/0046/0066
  - Guid v7 `…Identifier` records: ADR-0039/0090
  - `Result<T, ApiError>`: ADR-0047/0089
  - `Ensure.That`: ADR-0105
  - `Option<T>` in Domain/Application: ADR-0141
  - per-aggregate Domain folder with `Events/`: ADR-0092
  - per-message-kind Application folders: ADR-0093
  - domain events separate from `V<N>` integration events: ADR-0040/0073
  - two-layer optimistic concurrency with `If-Match`: ADR-0043/0113
  - `Idempotency-Key` on creates: ADR-0142
  - no automatic POST retry: ADR-0143
  - SignalR: ADR-0152
  - Postgres outbox and the durable inbox: ADR-0088/0126
  - Aspire fixture, no Testcontainers: ADR-0103
  - red-first testing: ADR-0139, constitution §Testing
- **No new ADR is written here.** PD-3 proposes reading ADR-0157 §1 in a way that departs
  from its literal text. If a human confirms that reading, ADR-0157 needs a one-paragraph
  amendment before merge. An agent may not write it (ADR-0144).

**Latency budget (constitution §IV): no leg is spent.** A scene switch is not on the
event-to-overlay path. It carries no overlay state and no plant-floor label. It does
create a measurable product concern: a wall re-negotiates N WebRTC sessions at once. That
concern is captured as a **Phase-5 verification requirement** (§7, FR-V1). It is not a
budget leg, and this spec makes no design decision about it.

**Phase-4a colour: red.** This is new behaviour throughout. The one refactor inside it,
extracting the kiosk grid renderer from `CellPage` (plan §6.3), is behaviour-preserving
and gets **characterisation** tests, observed green before the change.

---

## 1. Ground truth (verified against the code, 2026-09-26)

1. **There is no "wall" entity today.** ADR-0112 §1 made "wall" and "cell" the *same*
   `Layout` aggregate at different tile counts. The kiosk addresses what it shows by
   `/layouts/:layoutIdentifier` (`apps/kiosk-web/src/app/router.tsx`). A rotating wall
   shows *different* Layouts over time, so it needs an identity that is **not** a
   `LayoutIdentifier`. This fact drives PD-2.
2. **A chain holds at most one Published revision, and publishing archives the previous
   one** (`Layout.Publish`, `src/LayoutComposition/Domain/Layout/Layout.cs:208`). So any
   two distinct Published revisions belong to two distinct Layout chains. A reference
   pinned to a *specific* revision goes stale the moment its author re-publishes. This
   fact drives PD-3.
3. **`RuleAction` is a discriminated value object with two variants**, `SetVariableValue`
   and `HighlightOverlay` (`src/Automation/Domain/Rule/RuleAction.cs`). It is persisted
   packed into `action_packed` by `RuleActionColumnConverter`. Automation declares
   context-local copies of foreign identifiers, such as its own `OverlayIdentifier`,
   instead of referencing another context.
4. **Automation evaluates rules only on `FabEventIngestedV1`**
   (`FabEventIngestedV1Handler`). It has no scheduled, cron or timer trigger source
   anywhere: `grep -riE 'cron|schedul|timer|interval' src/Automation` returns nothing.
   ADR-0157 §2 says a schedule is "a `TriggerKind` Automation already has room for". That
   holds only for the string type. **Nothing produces scheduled trigger events.** See
   §3 and Risk R2.
5. **Effects do not carry the rule that produced them.** `OverlayHighlightRequestedV1`
   has `CausingEventIdentifier` but no rule identifier. ADR-0157 §Consequences requires
   the switch event to answer "*which rule*". `CompiledRule.Identifier` exists at
   evaluation time, so the rule identifier can be carried through the effect.
6. **The kiosk is view-only.** Constitution §VIII says it signs in as the shared public
   client with view-only scopes (`KeycloakScopeBundles.Kiosk`, which includes
   `sse.layouts.read` and no write scope), and kiosk-bound operator elevation is not
   built. This fact drives PD-4.
7. **LayoutComposition uses the durable Postgres inbox with one listener**
   (`AddWolverineForContext` defaults: `useNativeAcks: false`, `listenerCount: 1`). A
   redelivered `WallSceneSwitchRequestedV1` is therefore deduplicated before it reaches
   the handler. This matters because "Next" is not idempotent (plan §4.4).
8. **Audit is an explicit per-event list.** `IntegrationEventAuditHandler` has one
   `Handle` per contract, so a new `*V1` is not audited until a line is added there.

---

## 2. Decisions this spec proposes and a human must confirm

### PD-1 — Manual switch vs. an active rotation rule: which wins, and why?
**Decided (2026-09-26, product owner): last writer wins.** (ADR-0157 open question (a))
This binds US2 only; US1 has no rule path yet. **Must be revisited when a time-based
trigger is added** — see the "What changes this answer" paragraph below, which stands
as written.

**Last writer wins. There is no precedence and no "hold" in v1.** A manual
switch and a rule-driven switch are two requests against one piece of state. Whichever
reaches the `Wall` aggregate later sets the scene. Each switch is audited with its cause:
who did it or which rule did it, and which event triggered that rule. A manual switch
therefore stays on screen **until the next rule actually fires**. A rule does not
re-assert itself on its own.

**Reasoning:**

- **Event-driven rules fire on discrete occurrences.** They do not fire continuously.
  With the triggers that exist today (§1.4), a rule switches only when a new
  plant-floor event matches. That event is new information the operator may not have.
  If "manual wins", an alarm-driven switch would be silently suppressed because someone
  clicked "Next" earlier. That is the wrong failure in a 24/7 industrial control room.
- **"Rule always wins" is not coherent in this model.** A rule has no standing opinion
  about what should show between firings. There is nothing for it to "win" with, so it
  could only mean disabling the manual path whenever an Active rule targets the wall.
  That takes control away from the person in front of the wall.
- **The repo has no precedence concept to borrow.** Automation has no rule priority.
  Overlapping highlights are OR'd, not ranked (`OverlayHighlightRequestedV1` doc). A
  hold or pause would be a new domain concept: a duration value object, a release
  command, UI, and an audit shape. Constitution §IX and ADR-0036 say not to build it
  before the need exists.
- **What changes this answer:** a **time-based** trigger (§3, out of scope). Under a
  "rotate every 60 s" rule, last-writer-wins reverts an operator's choice within a
  minute, which is exactly the case where a manual hold earns its keep. **The spec that
  adds a scheduled trigger must reopen PD-1.** The recommended follow-up shape is "a
  manual switch pauses scheduled triggers only, for a bounded time or until released;
  event-triggered rules still apply". That shape is recorded here and not built.

**Alternatives the human may prefer instead:**

- (b) Manual wins for a fixed hold window of N minutes after a manual switch, during
  which rule-driven switches are recorded in the audit as `Suppressed` but not applied.
  This adds a `ManualHold` value object on `Wall`, a suppressed-switch audit event, and a
  UI indicator.
- (c) A per-wall toggle, "automatic switching paused", controlled only by an operator.

Choosing either one enlarges US2 by roughly one story.

### PD-2 — Where does "currently showing" live, and what shape is it?
**Decided (2026-09-26, product owner): a new, small aggregate.** (ADR-0157 open
question (b)) Including the name: **`Wall`**, as proposed — no rename.

**A new, small aggregate `Wall` in LayoutComposition**, at
`src/LayoutComposition/Domain/Wall/`. It owns **both** the ordered scene set **and** the
pointer to the scene currently showing. Nothing is added to `Layout` or `Revision`.

```
Wall : AggregateRoot<WallIdentifier>
  Fab           : FabIdentifier                 // fixed at creation (spec 017 precedent)
  Name          : WallName                      // unique per fab among live walls
  Scenes        : IReadOnlyList<LayoutIdentifier>  // ordered, 2..MaxScenes, no duplicates
  Showing       : LayoutIdentifier              // invariant: Showing ∈ Scenes
  SceneVersion  : SceneVersion                  // monotonic; +1 per applied switch
  ShowingSince  : ShowingSince                  // when Showing last changed
  Creation      : Creation
  Version       : AggregateVersion              // inherited; If-Match + EF token (ADR-0113)
```

**Reasoning:**

- **A field on `Layout` does not work, and it is not merely the less elegant option.**
  §1.1 shows that the thing that rotates is a set of Layouts. No single Layout can hold
  "which of us is showing". ADR-0157 §3 also forbids changing `Layout`/`Revision`.
- **The scene set and the pointer belong in one aggregate** because their invariant
  (`Showing ∈ Scenes`) spans both. Suppose they were split into a `SceneSet` and a
  separate `WallScene` state holder. Then removing the showing scene from the set would
  leave a dangling pointer, which only eventual consistency could repair. One aggregate
  gives one transaction, one `Version` for `If-Match`, and one EF concurrency token. That
  token is also what makes a manual switch and a rule switch that land at the same time
  race-safe, which ADR-0157 §3 requires.
- **`Showing` is a `LayoutIdentifier`, not an index.** An index changes meaning when the
  set is reordered. A layout reference does not, and `Scenes` forbids duplicates, so
  the reference is unambiguous.
- **`SceneVersion` lets the kiosk discard out-of-order frames.** It is the same device as
  the per-overlay `Version` on `ResolvedOverlayTextChangedNotification`. `Version`
  cannot serve this purpose because it also moves on scene-set edits.
- **"Cheap to read"** (ADR-0157 §3) means one row lookup by primary key.

**Naming to confirm:** ADR-0112's vocabulary calls a multi-tile `Layout` a "wall", and
management-web has a "wall designer". An aggregate named `Wall` shifts that word to "the
display surface that shows one of several Layouts". The alternatives are `SceneWall` or
`RotatingWall`. **Proposed: `Wall`**, with UI copy updated so "layout" means the
authored grid and "wall" means the surface. Confirm or pick another name. The name
propagates into routes, contracts and the audit resource map.

### PD-3 — A scene references a Layout chain, not a pinned Revision
**Decided (2026-09-26, product owner): confirmed.** ADR-0158 records the amendment to
ADR-0157 §1 this decision requires (written and accepted the same day).

ADR-0157 §1 says "an ordered list of references to Revisions". Taken literally, §1.2
makes that unworkable: each time a layout's author publishes an edit, the referenced
revision becomes Archived, and the scene would go dark or show stale content.

**Proposed:** a scene entry is a `LayoutIdentifier`. At display time it resolves to
**that chain's currently Published revision**, which is exactly what the kiosk already
does for `/layouts/:id`. Editing a layout that is part of a scene set then updates the
scene in place, which is what an author would expect. This keeps ADR-0157's intent intact
(no new content model; selection among independently published layouts) and changes
only what "reference" points to.

### PD-4 — Who may switch a wall by hand, and from where?
**Decided (2026-09-26, product owner): management-web only, existing
`sse.layouts.write` scope.** No new Keycloak scope for v1.

**From v1:** from **management-web only**, gated by the **existing
`sse.layouts.write`** scope. This needs no new scope and no Keycloak realm change.
**Switching from the kiosk is deferred.** The kiosk is view-only by constitution §VIII,
and kiosk-bound operator elevation does not exist (§1.6).

**Explicit non-goal for v1:** a narrower `sse.walls.operate`-style scope for shift
operators who must not hold `sse.layouts.write` is **not built now**. If that persona's
need becomes real, it is a follow-up issue — a realm change, a bundle change,
`KioskScopeParityTests` implications, and a volume reset for anyone running the stack
locally, none of which is warranted speculatively (constitution §IX).

### PD-5 — The maximum number of scenes per wall
**Decided (2026-09-26, product owner): `MaxScenes = 8`, `MinScenes = 2`, as proposed.**
**Still unmeasured** — this is a UI-scale guess, not a latency-derived figure, and the
product owner may revise it freely later; nothing below depends on 8 being exactly right.

**`MaxScenes = 8`, `MinScenes = 2`.** A wall with one scene is simply a Layout
and has nothing to switch between. The upper bound exists so that the set, the editor
and the audit payload stay bounded. **8 has no measurement behind it.** It is a UI-scale
guess, and the product owner may change it freely. Unlike `MaxTiles`, it has no latency
consequence, because only one scene is decoded at a time.

### PD-6 — Scenes whose layout is not Published
**Decided (2026-09-26, product owner): confirmed as proposed.**

A scene's layout can be reverted to Draft or archived after the wall is configured.

**Confirmed:**

- **"Next" skips** any scene whose layout has no Published revision at the moment of the
  switch, and wraps cyclically. If no other scene is publishable, the switch is a no-op.
- **"Jump to scene X"** where X has no Published revision is refused: `409
  WALL_SCENE_NOT_PUBLISHED` for a manual switch. For a rule, the request is dropped with
  a structured warning log and no event.
- **The scene currently showing becomes unpublished.** The kiosk shows the placeholder it
  already shows for an archived layout. The wall does **not** auto-advance, because an
  automatic switch nobody asked for is exactly the unattributed change ADR-0157 warns
  about.
- **Configuring a wall requires every referenced layout to be Published** at create or
  edit time (`409 WALL_SCENE_NOT_PUBLISHED`). That rule keeps new walls sane, and the
  rules above handle any drift afterwards.

---

## 3. Scope

**In scope for this delivery:**

- **US1 (P1).** Define a wall, switch it by hand, and have the kiosk follow live.

**Parked (its own follow-up issue, filed at Phase 3 gate — not this PR):**

- **US2 (P2).** An Automation rule switches a wall. Blocked on PD-1's confirmed answer
  already being in place (it is — last writer wins) **and** on US1 shipping first, since
  US2 depends on its contract. Kept fully specified below (§4, User Story 2) so the
  follow-up issue can point straight at it rather than re-deriving the design.

**Out of scope, with where each item goes:**

- **Time-based rotation (a scheduled trigger).** ADR-0157 §2 treats this as "one trigger
  source among several". §1.4 shows that no scheduled trigger source exists, so it is
  new infrastructure: something must own a clock and emit trigger events, and that is a
  design question in its own right (Wolverine scheduled messages? a per-fab ticker? a
  cron `TriggerSource` Automation polls?). **It needs its own spec, and probably an ADR
  note on where the clock lives. It must reopen PD-1.**
- Operator switching from the kiosk (PD-4).
- Archiving or deleting a wall. v1 walls can be edited but not retired, and this is a
  known gap: a follow-up issue is filed at Phase 3.
- Keeping a WebRTC session alive across a switch when the camera at a tile position is
  unchanged. That is an optimisation candidate, informed by the FR-V1 measurement, and
  not a v1 requirement.
- Inter-display synchronisation of switches across several kiosks. Each kiosk follows the
  push independently, and PTP remains out of scope (ADR-0128).

**Slicing.** US1 is the smallest vertical that can be shipped and observed end to end on
its own: a wall, its state, a manual switch, the integration event, the audit row, and
the kiosk following live. US2 adds the Automation action on top and depends on US1's
contract. **This delivery is US1 only.** US2 is fully specified (§4) for the follow-up
issue filed at the Phase 3 gate, but not built here — PD-1 being confirmed removes that
particular blocker, but US2 still needs US1's contract to exist first, and stays parked
until then per the product owner's own sequencing call.

---

## 4. User scenarios and testing

### User Story 1 — An admin defines a wall and switches it by hand; the kiosk follows (P1)

A fab admin opens management-web and goes to **Walls** → **New wall**. They name the
wall and pick an ordered list of Published layouts as its scenes, then save. The wall
shows its first scene. A kiosk opens the wall at `/walls/:wallIdentifier`, or picks it
from the picker, and renders the first scene's grid. In management-web the admin clicks
**Next**, or **Show** on a specific scene. Within about a second the kiosk tears down the
old grid and renders the new scene. The audit log shows the switch with the admin as the
actor.

**Why P1:** it builds the `Wall` aggregate, the current-state pointer, the
`WallSceneChangedV1` event, the audit line, the SignalR frame and the kiosk route. Every
new element US2 needs is proven without Automation involved.

**Independent test:**

1. `aspire run`. Sign in to management-web as admin. You need at least 2 Published
   layouts in the same fab, using the existing spec 010 flow. The Scenario Simulator's
   seeded walls work.
2. Go to **Walls** → **New wall**. Name it "Line 3 rotation" and pick layout A, then
   layout B. Save. The wall shows **Showing: A**.
3. Open kiosk-web and pick "Line 3 rotation". The kiosk renders layout A's grid.
4. In management-web, click **Next**. Within ≤ 1 s, the kiosk renders layout B's grid.
   Management-web shows **Showing: B**.
5. Click **Show** on A. The kiosk returns to A.
6. Open the audit log. It has two `WallSceneChangedV1` rows naming the admin, the
   previous scene and the new scene.

**Acceptance scenarios** (Gherkin):

```gherkin
Scenario: US1-1 Create a wall (happy path)
  Given an admin with sse.layouts.write in fab F
    And Published layouts A and B in fab F
  When the admin POSTs /walls { name: "Line 3 rotation", scenes: [A, B] } with an Idempotency-Key
  Then the response is 201 Created with the wall identifier
    And GET /walls/{id} returns scenes [A, B], showing A, sceneVersion 0, and an ETag version
    And WallConfiguredV1 is published and audited

Scenario: US1-2 Create is replay-safe
  Given the same request and Idempotency-Key as US1-1 is sent again
  Then the response is the original 201 with the same wall identifier
    And no second wall exists

Scenario: US1-3 Manual "next" switches the wall and the kiosk follows
  Given wall W showing A with scenes [A, B] at version v
    And a kiosk in fab F rendering W
  When the admin POSTs /walls/{W}/switch { target: "next" } with If-Match: v
  Then the response is 200 OK with showing B and sceneVersion incremented by 1
    And WallSceneChangedV1 { Wall: W, PreviousLayout: A, CurrentLayout: B, Cause: "Operator" } is published with Metadata.Actor = the admin
    And the kiosk receives a WallSceneChanged frame and renders B's grid
    And an audit row records the switch

Scenario: US1-4 Jump to a named scene
  Given wall W showing A with scenes [A, B, C]
  When the admin POSTs /walls/{W}/switch { target: "layout", layout: C } with a current If-Match
  Then W shows C

Scenario: US1-5 "Next" wraps around
  Given wall W showing the last scene C of [A, B, C]
  When the admin switches "next"
  Then W shows A

Scenario: US1-6 Switching to the scene already showing is a no-op
  Given wall W showing A
  When the admin switches to layout A
  Then the response is 200 OK with sceneVersion unchanged
    And no WallSceneChangedV1 is published

Scenario: US1-7 Conflict — stale If-Match
  Given wall W at version v+1 (someone else switched it)
  When the admin POSTs /walls/{W}/switch with If-Match: v
  Then the response is 409 WALL_STALE and W is unchanged

Scenario: US1-8 Conflict — the target scene's layout is no longer Published (PD-6)
  Given wall W with scenes [A, B] where B has since been archived
  When the admin switches to layout B
  Then the response is 409 WALL_SCENE_NOT_PUBLISHED

Scenario: US1-9 Conflict — duplicate wall name in the fab
  Given a live wall named "Line 3 rotation" in fab F
  When the admin creates another wall with that name in fab F
  Then the response is 409 WALL_NAME_TAKEN

Scenario Outline: US1-10 Bad request — invalid scene sets
  When the admin POSTs /walls with scenes <scenes>
  Then the response is 400 <code>
  Examples:
    | scenes                     | code                     |
    | [A]                        | WALL_TOO_FEW_SCENES      |
    | 9 distinct layouts         | WALL_TOO_MANY_SCENES     |
    | [A, A]                     | WALL_DUPLICATE_SCENE     |
    | [A, layout-from-fab-G]     | WALL_SCENE_OTHER_FAB     |
    | [A, unknown-guid]          | WALL_SCENE_NOT_FOUND     |

Scenario: US1-11 Bad request — the switch target is not in the set
  When the admin switches wall W to a layout that is not among its scenes
  Then the response is 400 WALL_SCENE_NOT_IN_SET

Scenario: US1-12 Bad request — missing If-Match on switch
  When the admin POSTs /walls/{W}/switch without If-Match
  Then the response is 428 (the existing ADR-0113 precondition-required behaviour)

Scenario: US1-13 Auth — a kiosk token cannot switch
  Given a caller holding only the kiosk scope bundle (sse.layouts.read)
  When it POSTs /walls/{W}/switch
  Then the response is 403
    And GET /walls/{W} with the same token returns 200

Scenario: US1-14 Auth — another fab's wall is invisible
  Given an admin whose fab groups do not include fab F
  When they GET or switch wall W in fab F
  Then the response is 404 (not 403 — existence is not disclosed, as for layouts)

Scenario: US1-15 Removing the showing scene moves the pointer
  Given wall W showing B with scenes [A, B, C]
  When the admin PUTs /walls/{W}/scenes [A, C] with a current If-Match
  Then W shows A (the new first scene)
    And WallSceneChangedV1 with Cause "Reconfigured" and Actor = the admin is published

Scenario: US1-16 Kiosk ignores out-of-order frames
  Given the kiosk has rendered sceneVersion 5
  When a WallSceneChanged frame with sceneVersion 4 arrives
  Then the kiosk keeps rendering the sceneVersion-5 scene

Scenario: US1-17 Kiosk reconciles after a reconnect
  Given the kiosk's hub connection dropped while the wall was switched
  When the connection is re-established
  Then the kiosk re-reads GET /walls/{W} and renders the current scene
```

---

### User Story 2 — An Automation rule switches a wall (P2, blocked on PD-1)

A fab admin authors a rule: *when a `plc` / `line-stop` event arrives with `$.payload.line
== 3`, switch wall "Line 3 rotation" to layout "Line 3 fault view"*. They publish the
rule. When a matching event is ingested, the wall switches, and the audit row names the
rule and the triggering event.

**Independent test:**

1. With US1's wall in place, create a rule with action type **Switch wall scene**:
   wall = W, target = layout B, trigger `plc`/`line-stop`, predicate `$.payload.line ==
   3`. Publish it.
2. `POST /events` a matching `plc`/`line-stop` event. The kiosk switches to B.
3. The audit shows `WallSceneChangedV1` with `Cause: "Rule"`, the rule identifier, and
   the causing event identifier. `Metadata.Actor` is null.
4. Switch manually back to A (US1). Ingest another matching event. The wall switches to B
   again. This is PD-1's last-writer-wins behaviour, observed.

**Acceptance scenarios:**

```gherkin
Scenario: US2-1 Create a rule with a SwitchWallScene action (happy path)
  Given an admin with sse.rules.write in fab F
  When the admin POSTs /rules { actionType: "SwitchWallScene", wallIdentifier: W, sceneTarget: "Layout", targetLayoutIdentifier: B, ... }
  Then the response is 201 and GET /rules/{id} round-trips the action exactly

Scenario: US2-2 A matching event switches the wall
  Given an Active rule R in fab F targeting wall W, layout B
    And W shows A
  When a FabEventIngestedV1 in fab F matches R
  Then WallSceneSwitchRequestedV1 { Wall: W, Target: "Layout", TargetLayout: B, Rule: R, CausingEventIdentifier: e } is published
    And W shows B
    And WallSceneChangedV1 { Cause: "Rule", Rule: R, CausingEventIdentifier: e } is published and audited

Scenario: US2-3 "Next" target from a rule
  Given an Active rule with sceneTarget "Next" on wall W showing A of [A, B]
  When it fires
  Then W shows B

Scenario: US2-4 Manual then rule — last writer wins (PD-1)
  Given W was switched manually to A after rule R last switched it to B
  When R fires again
  Then W shows B

Scenario: US2-5 Rule then manual — last writer wins (PD-1)
  Given rule R switched W to B
  When the admin switches W to A
  Then W shows A and stays on A until R fires again

Scenario: US2-6 Concurrent manual and rule switch
  Given a manual switch and a rule-driven switch reach wall W in the same instant
  Then exactly one is applied first, the other is applied on top of it (the rule path re-reads on a concurrency conflict), sceneVersion rises by the number of switches that changed the scene, and there is no lost update or torn state

Scenario: US2-7 Bad request — incomplete action
  When the admin POSTs /rules with actionType "SwitchWallScene" and sceneTarget "Layout" but no targetLayoutIdentifier
  Then the response is 400 with the existing typed rule-validation error

Scenario: US2-8 Rule in another fab cannot move the wall
  Given wall W in fab F and a rule in fab G that names W
  When the rule fires
  Then W is unchanged, no WallSceneChangedV1 is published, and a structured warning is logged

Scenario: US2-9 A rule naming an unknown wall
  When a rule targeting a non-existent wall fires
  Then nothing changes and a structured warning is logged (same as US2-8)

Scenario: US2-10 A duplicate delivery does not advance twice
  Given a WallSceneSwitchRequestedV1 with target "Next" is delivered twice (broker redelivery)
  Then the wall advances exactly once

Scenario: US2-11 Auth — rule creation needs sse.rules.write
  When a caller without sse.rules.write POSTs a SwitchWallScene rule
  Then the response is 403
```

**Known weakness, accepted for v1 and listed as R3:** Automation cannot check at
rule-creation time that the wall exists or belongs to the rule's fab. It holds no
reference to LayoutComposition, the same as `HighlightOverlay` today. A mistyped wall
identifier fails only at firing time, as a log line (US2-9).

---

## 5. Functional requirements

- **FR-001** A `Wall` belongs to one fab, fixed at creation. Every scene layout must be in
  that fab.
- **FR-002** A wall has 2..`MaxScenes` scenes (PD-5), ordered, with no duplicates. Each
  scene is a `LayoutIdentifier` (PD-3).
- **FR-003** `Showing` is always a member of `Scenes`. On creation it is the first scene.
  Editing out the showing scene moves `Showing` to the new first scene, with `Cause:
  Reconfigured` (US1-15).
- **FR-004** A switch that changes `Showing` increments `SceneVersion` by exactly 1,
  updates `ShowingSince`, and raises one domain event, which becomes one
  `WallSceneChangedV1`. A switch that changes nothing raises nothing.
- **FR-005** A manual switch requires `If-Match` (ADR-0113). A rule-driven switch does
  not: it applies to whatever is current, per PD-1.
- **FR-006** `WallSceneChangedV1` names the cause without any timestamp correlation. For
  an operator switch, `Metadata.Actor` names the operator. For a rule switch, the event
  carries the rule identifier and the causing event identifier (ADR-0157
  §Consequences).
- **FR-007** Every new `*V1` is added to `IntegrationEventAuditHandler` and the V1
  resource map.
- **FR-008** The kiosk subscribes to `WallSceneChanged` on `/hubs/layouts`, scoped to its
  fab group. It discards frames with a `SceneVersion` lower than or equal to the one it
  is rendering, and re-reads the wall on reconnect.
- **FR-009** A rule's `SwitchWallScene` action round-trips through persistence, the API
  DTO and dry-run unchanged. Dry-run reports that the action fires but produces no value,
  as `HighlightOverlay` does today.
- **FR-010** LayoutComposition drops a `WallSceneSwitchRequestedV1` whose
  `Metadata.Fab` differs from the wall's fab, whose wall does not exist, or whose target
  is not publishable (PD-6). It logs a structured warning via `[LoggerMessage]`,
  **naming the wall identifier the request targeted** (so a mistyped or stale reference
  in a rule is debuggable from the log line alone, without correlating timestamps against
  the rule definition), and publishes no event.

**Phase-5 verification requirement**, which is not a design requirement:

- **FR-V1** The scene switch must be *observed and measured*, not only tested (§7).

## 6. Success criteria

- **SC-001** Every US1 and US2 acceptance scenario above is an automated test. Domain
  and Application tests cover the invariants. The Aspire-fixture integration covers the
  HTTP, messaging and audit paths. Playwright covers US1-3/US1-17 on the kiosk and
  US2-2.
- **SC-002** Coverage gates hold: Domain ≥ 90%, Application ≥ 80% (ADR-0065).
- **SC-003** `PrimitiveBoundaryTests`, `HandlerDeconstructionTests` and the NetArchTest
  boundary rules pass with no new suppression or carve-out.
- **SC-004** FR-V1's figures are recorded in `verification.md`.

## 7. Phase-5 verification: the scene-switch settle check (FR-V1)

ADR-0157 §Consequences and issue #2608 name this concern. It is **not a §IV leg**, and
this spec sets **no pass/fail threshold**. Whether one is wanted is a follow-up product
question, and the measurement below is what would inform it. Phase 5 must:

1. On a kiosk rendering a wall whose two scenes are each a **fully-populated grid at the
   current cap** (4 tiles; and 9 tiles if ADR-0156's cap has shipped by then), with
   **disjoint cameras** between the two scenes as the worst case, switch the wall at
   least **20 times**.
2. Record per switch: *settle time*, meaning the time from the kiosk receiving the
   `WallSceneChanged` frame until **every** tile of the new scene has painted a live
   frame (the same "live" signal spec 094 uses), and *blank time*, meaning the longest
   period any tile position shows no picture.
3. Report p50, p95 and max for both figures, the hardware used, and a qualitative note on
   visible stutter. Compare them against spec 002's ≤ 3 s p95 click-to-first-frame per
   tile, **for context only**.
4. Run it twice. The first run after machine churn misleads.
5. Confirm that the event-to-overlay path is not disturbed: a highlight fired during a
   switch still lands on the new scene's matching tiles, and on no stale tile.

If the figures are bad, the outcome is a finding and an issue for the optimisation in
§3's out-of-scope list. It is not a silent redesign in this spec.

## 8. Risks and open items for the human

- **R1 — ADR-0157 is not merged.** PR #2606 is open. This spec references a file that
  `develop` does not have. **Merge #2606 first**, then rebase this branch onto
  `develop`. The alternative is stacking this branch on #2606, but per `CLAUDE.md` do
  that only if it cannot wait.
- **R2 — ADR-0157 §2's schedule claim.** "A `TriggerKind` Automation already has room
  for" is true of the type but not of the system (§1.4). Time-based rotation is new
  infrastructure. It may warrant a short ADR addendum, because the ADR currently reads
  as though it were free.
- **R3 — Rules can name walls that do not exist.** See US2's known weakness. The cost is
  a silent no-op at firing time. A LayoutComposition-owned wall read model inside
  Automation would fix it, but that is a larger change and is deferred.
- **R4 — The meaning of "wall" shifts** (PD-2 naming). UI copy and docs that call a
  multi-tile Layout a "wall" will read ambiguously until they are updated.
- **R5 — `Next` is not idempotent.** It depends on LayoutComposition keeping the durable
  inbox (§1.7). If that context later opts into native acks (ADR-0126), US2-10 is the
  test that catches it. The test must stay.
