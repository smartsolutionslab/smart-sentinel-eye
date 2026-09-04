# Implementation Plan: A variable change reaches the tile

**Spec**: `specs/063-a-variable-change-reaches-the-tile/spec.md`

**Issue**: #2012 · **Branch**: `fix/2012-a-variable-change-reaches-the-tile`

**Lane**: ADR-0144 autonomous.

---

## The three declarations (ADR-0144)

### Declaration 1 — which engineer

**A mix, and the split is clean.**

| Work | Agent | Why |
|---|---|---|
| The fix — two lines in two C# handlers | **`backend-engineer`** | SystemVariables Application layer |
| Red unit tests on both producers | **`test-writer`** | xUnit, Shouldly, existing fakes |
| Red integration test — a real hub frame off the real stack | **`test-writer`** | Aspire fixture; the pattern already exists |
| Cross-fab negative case, bad-request case | **`test-adversary`** | the issue is about a silent failure mode; the cases that must *not* fire are where a naive fix goes wrong |
| Component test — the tile re-renders on a push | **`test-writer`** | Vitest + Testing Library, `apps/kiosk-web` |
| Un-`fixme` the e2e span and observe it red | **`test-writer`** | one word; the wall fixture already exists |
| Contingency: a frontend defect found by T004 | **`frontend-engineer`** | only if T004 comes back red |
| Review | **`backend-reviewer`** + **`security-reviewer`** | the change alters what is delivered across fab boundaries |

**`security-reviewer` is not optional here.** The field being populated is the
one that decides which plant's screens see a figure. The failure direction that
matters is not "still doesn't update" — it is "updates on the wrong wall".

**`infra-engineer`: none.** No AppHost, CI, Keycloak or gateway change.

### Declaration 2 — is the honest answer a new ADR?

**No. This is a plain defect against decisions already made, and it is not a
block.**

Every element of the intended behaviour is already decided in writing:

- **ADR-0102** decides that every integration event carries `EventMetadata`, and
  documents `Fab` as *"Owning fab when the event is fab-scoped; otherwise null"*.
  `ResolvedOverlayTextChangedV1` **is** fab-scoped — ADR-0115 says the same
  overlay resolves to different values per fab. So the correct value of that
  field was decided before the field was left null.
- **Spec 017 FR-015 / ADR-0115** decide that the push goes to the fab the change
  happened in and nowhere else. The consumer implements that decision correctly.
- **Spec 005 FR-013** decides that the resolved text is pushed on
  `/hubs/layouts`.

Nothing here requires a new decision, a changed decision, or a decision that was
never made. The producer simply fails to supply a value an existing decision
requires. **Manufacturing a block to preserve a pattern of blocks would be as
dishonest as softening a real one.**

**Two things the lane must NOT do, recorded so they are not done by accident:**

1. **Constitution §IV must not be edited by this run.** §IV currently records
   *event → overlay state* as "recorded, not yet readable" and adds that it is
   "now also suspected broken for an already-open tile". This work locates and
   fixes that. Updating either the prose or the *Measured* cell is a constitution
   amendment, reserved to a human by ADR-0144. The PR states what changed and
   what was measured; the human decides what §IV should say.
2. **The sibling defect must not be folded in.** `SystemVariableArchivedV1`'s own
   null fab (spec.md, *Out of scope*) is a separate bug with a separate symptom.
   Recommend a separate issue.

### Declaration 3 — behaviour-changing or behaviour-preserving

**Behaviour-changing. Phase 4a is RED.**

Unambiguously so: a frame that is not sent today is sent after the change, and a
tile that does not update does. There is no reading of this as a refactor, so
ADR-0144's tie-break ("ambiguity resolves to behaviour-changing") is not even
reached.

**The characterisation path is explicitly wrong here** and must not be
substituted: characterisation locks in current behaviour *including its defects*,
which would encode "the tile never updates" as a safety net.

---

## Phase 4a design — the shape the change lands in

### The fix

Two files, two lines. Both already hold the fab in a local from the handler's
opening deconstruction.

**`src/SystemVariables/Application/EventHandlers/VariableValueChangedDomainEventHandler.cs`**,
line 72 — inside the `foreach (Guid overlayId in affectedOverlays)` loop:

```
-                    null,
+                    fab.Value,
```

**`src/SystemVariables/Application/EventHandlers/VariableArchivedDomainEventHandler.cs`**,
line 104 — same position, same loop:

```
-                    null,
+                    fab.Value,
```

`fab` is a `FabIdentifier` destructured at the top of each `Handle` method and
already used elsewhere in both (`variables.GetByNameAsync(changed.Fab, …)` /
`(fab, parsed, …)`), so nothing new is introduced, imported or injected.

**Positional, not named.** Writing `Fab: fab.Value` was considered — the
unnamed `null` in position three is a large part of why this was invisible — and
rejected: the `SystemVariableValueChangedV1` construction **four lines above**
passes its metadata positionally, and a single named argument in one of two
adjacent, otherwise-identical constructions reads as significance where there is
none. The unit test is what makes the field non-optional from now on. (CLAUDE.md:
mirror existing patterns rather than introducing new ones unjustified.)

### Nothing else changes

- The consumer's guard stays exactly as it is (FR-007). It is correct, it is
  tested, and it is the thing that keeps a fab-less frame off every wall.
- The broadcaster, the hub, the group naming, the contract record, and the whole
  frontend are untouched.
- No migration, no new DI registration, no new package.

### Why the smallest change is the right one here

The two alternatives both fail on inspection:

- **Broadcast to all when the fab is missing.** Turns a "no update" bug into a
  cross-fab information leak. Forbidden by ADR-0115 and spec 017 FR-015.
- **Derive the fab in LayoutComposition** from the overlay's referencing layouts.
  LayoutComposition genuinely knows which fabs reference an overlay — it does
  this for `OverlayRevisionPublished`. But the resolved *text* is fab-specific:
  one munich value would fan out to every fab that references the overlay, each
  receiving munich's figure. The producer is the only party that knows which
  fab's value this is. It already knows it. It just does not say so.

---

## The red test — three levels, and what each honestly proves

The instruction to be sceptical of a cheap test standing in for a real one is
taken literally. Each level is stated with what it **cannot** prove.

### Level 1 — unit (xUnit), the producers

`tests/SystemVariables.Application.Tests/EventHandlers/VariableValueChangedDomainEventHandlerTests.cs`
and `…/VariableArchivedDomainEventHandlerTests.cs`.

Both files already build the domain event with `FabIdentifier.From("munich")` and
already pull the published `ResolvedOverlayTextChangedV1` out of the fake bus.
The new assertion is one line:

```
push.Metadata.Fab.ShouldBe("munich");
```

- **Red today**, on `Expected "munich" but was null`.
- **Proves:** the producer stamps the fab that changed.
- **Does not prove:** that the consumer's guard passes, that a frame is
  broadcast, that a group is addressed, or that anything renders.
- **Cost:** ~85 ms; already-passing suite; no Docker.

**This level alone is exactly the trap to avoid.** It is the cheapest possible
green tick over this defect and it would have been satisfied by the null too, had
anyone written it a day earlier — it only catches this because the assertion is
about the value, not the presence.

### Level 2 — integration (Aspire fixture), the seam that had no test

**New file:** `tests/Integration.Tests/SystemVariables/ResolvedTextReachesItsFabTests.cs`

Modelled directly on `tests/Integration.Tests/LayoutComposition/OverlayFrameFabScopingIntegrationTests.cs`,
which already does the hard parts: two real `HubConnection`s, one per fab, tokens
minted per operator, a `ConcurrentDictionary<Guid, TaskCompletionSource<…>>` per
connection, and a stated `SilenceWindow` for the frame that must not arrive.

Shape:

1. Define a variable in munich; publish an overlay whose text embeds its
   placeholder; reference the overlay from a published munich layout.
2. Wait until the overlay is resolvable — the reverse index is populated by an
   integration event, so publish returning is not enough.
   `NFR_VariableResolutionLatencyTests.WaitUntilResolvableAsync` is the existing
   precedent and says why.
3. Open a hub connection with a munich token and one with a dresden token.
4. `PUT /system-variables/{name}/value`, stamping `Stopwatch` when it returns.
5. **Assert the munich connection receives a `ResolvedOverlayTextChanged` frame
   carrying the new resolved text**, and stop the clock. Print the figure.
6. **Assert the dresden connection received nothing** within the silence window.

- **Red today** at step 5: the frame never arrives, because the handler returned
  early. It fails on a timeout, which is a weaker failure signal than an equality
  mismatch — so the assertion carries an explicit message naming the log line to
  look for (`ResolvedOverlayTextChangedWithoutFab`), and step 6 is *not* what
  makes it red.
- **Proves:** the whole server-side path — domain event → outbox → RabbitMQ →
  LayoutComposition subscriber → the fab guard → the broadcaster → a SignalR
  group → a real subscribed client — and that it reaches that fab **only**.
  This is the seam the nine green unit tests left uncovered, and it is where the
  defect lived.
- **Does not prove:** that a React tile re-renders. It is a .NET client on a
  socket, not a browser.
- **Cost:** the `integration` job's Docker stack, which the PR pays for anyway.
- **FR-006 lives here**: this is where the leg figure comes from, both stamps in
  one process on one clock.

### Level 3 — component (Vitest), the hop nothing has ever run

`apps/kiosk-web/src/features/cell/CellPage.test.tsx` — a 17th test in a file
whose 16 existing ones never mention resolved text.

Render `CellPage` with a layout whose tile binds an overlay with a `{{…}}`
placeholder and a seeded snapshot; capture the `onResolvedOverlayTextChanged`
callback the way `useLayoutLifecycle.test.tsx` already captures it; invoke it with
a higher `version`; assert the rendered `camera-viewer-overlay-label` text
changes.

- **Expected GREEN on first run, and that is declared in advance rather than
  discovered.** It covers a path believed correct, not new behaviour. It is not
  the phase-4a red artifact and must not be presented as one.
- **If it is red, we have found a second, independent defect.** The
  `test-writer` reports its colour verbatim either way and does **not** adjust
  the test to match what the code does — that is the one thing ADR-0144 forbids
  in both colours. A red here escalates to `frontend-engineer` at 4b and is
  called out in the PR as a second cause.
- **Proves:** that CellPage's callback → `upsertQueryData` → the tile's snapshot
  query → the label is wired.
- **Does not prove:** anything about a real frame, a real hub, real video, or the
  label hold, all of which are stubbed in jsdom.

### Level 4 — e2e (Playwright), the only level that involves a tile

**One word.** `e2e/kiosk-shows-a-label-over-video.spec.ts`:

```
-test.fixme('the span from a value being submitted to it being visible', …
+test('the span from a value being submitted to it being visible', …
```

This is the check the issue is filed about. It signs a real kiosk into a real
wall with **real video decoding behind the tile**, waits for the live channel,
opens a second browser context as an operator, sets the variable five times, and
asserts the tile's label follows each time.

- **Red today** at iteration 0, with the recorded refusal:
  `iteration 0: the value never reached the tile within 60000ms`.
- **Proves — and it is the only level that does:** that an operator's change
  reaches a tile that was already on screen, in a real browser, over a real hub,
  with video running, unaided by any reload. Everything the issue reports, in one
  check.
- **Does not prove:** the 800 ms SLO. Its own docblock is explicit: it covers two
  legs of six, and carries **~±1000 ms of instrument error** — five times the
  200 ms leg. It is a *behaviour* check that also prints figures, not a
  measurement instrument for the leg.
- **Cost:** the `e2e` job's full-stack boot, plus a red run and a green run.

### Why the cheap levels do not stand in for the expensive one

They are not a substitute and the spec does not treat them as one. Level 1 is
green over a broken product today's suite already demonstrates that. Level 2
would have caught this defect and will catch its return, but a green Level 2 with
a broken CellPage still means a dark wall. **Level 4 is what makes the claim the
issue asks for**, and it is the one that stays in the suite un-`fixme`d
afterwards, which is the durable outcome: the path stops being untested.

This repository's recorded failures — #2054's green guard over a diagnostic that
never worked, #2061's report asserting a state nobody read — are exactly the
shape of "the cheap level passed, so we shipped". Hence four levels, each with
its limits written down before the run rather than after.

---

## Architecture

### Bounded contexts and layers

| Context | Layer | Change |
|---|---|---|
| **SystemVariables** | Application / EventHandlers | the two lines |
| LayoutComposition | Application, Infrastructure | **none** — consumer, hub, broadcaster untouched |
| Shared.Contracts | — | **none** — `EventMetadata` and the event record are unchanged; no `V2` |
| kiosk-web | — | **none** expected (see Level 3 contingency) |

### Entities, value objects, invariants

No domain model changes. `FabIdentifier` is an existing value object; `.Value`
crosses into `Shared.Contracts`, which is the documented wire boundary where
primitives are permitted (ADR-0040, constitution §II's exemption). No new
primitive appears on any domain model, so `PrimitiveBoundaryTests` is unaffected.

### Messaging

`VariableValueChangedDomainEvent` (in-process) → handler → **`ResolvedOverlayTextChangedV1`**
(integration, `Shared.Contracts`, Wolverine + Postgres outbox) → LayoutComposition
subscriber → `ILayoutLifecycleBroadcaster` → `Clients.Group("fab:<fab>")` →
`ResolvedOverlayTextChanged` frame.

The only change is the value of one field on the integration event. Contract
shape, routing, queue and outbox behaviour are all unchanged — additive in
neither direction, because the field already exists and is already serialised.

### Boundary rules

No new project reference. SystemVariables continues to know nothing about
SignalR; LayoutComposition continues to know nothing about resolution. The
existing NetArchTest rules are unaffected.

### Side effect worth naming for the reviewer

Audit rows for `ResolvedOverlayTextChangedV1` will begin carrying a `fab_id`.
`SearchAuditQueryHandler` treats a null fab as visible to every caller
(`Fab == null || allowed.Contains(Fab)`), so these rows become **fab-scoped**
where they were previously unscoped. That is a correctness improvement in the
same direction as the fix, and it is stated here so it is not discovered as a
surprise in a later audit query.

### Files touched

| File | Change |
|---|---|
| `src/SystemVariables/Application/EventHandlers/VariableValueChangedDomainEventHandler.cs` | one line |
| `src/SystemVariables/Application/EventHandlers/VariableArchivedDomainEventHandler.cs` | one line |
| `tests/SystemVariables.Application.Tests/EventHandlers/VariableValueChangedDomainEventHandlerTests.cs` | one assertion |
| `tests/SystemVariables.Application.Tests/EventHandlers/VariableArchivedDomainEventHandlerTests.cs` | one assertion |
| `tests/Integration.Tests/SystemVariables/ResolvedTextReachesItsFabTests.cs` | **new** |
| `tests/Integration.Tests/SystemVariables/NFR_VariableResolutionLatencyTests.cs` | docblock cross-reference (FR-008) |
| `apps/kiosk-web/src/features/cell/CellPage.test.tsx` | one test |
| `e2e/kiosk-shows-a-label-over-video.spec.ts` | `test.fixme` → `test` |

Eight files, of which two are production code and six are tests or a comment.

---

## Constitution and ADR alignment

| Rule | How this complies |
|---|---|
| §II value objects | no primitive added to a domain model; `.Value` at the contracts boundary only |
| §IV latency | leg named, effect stated, figure owed and sourced (spec.md) |
| §Testing — new behaviour starts red | levels 1, 2 and 4 are red first; level 3 declared green in advance with its reason |
| §VII dashboards | unchanged; pre-existing debt, not created here |
| ADR-0030 Conventional Commits | see tasks.md |
| ADR-0086 no `Co-Authored-By` | none in any commit |
| ADR-0036 smallest change | two lines; the sibling defect and the §IV edit both explicitly excluded |
| ADR-0105 guards | `Ensure.That` unchanged; no new guard |
| ADR-0144 lane limits | no ADR written, no constitution edit, no gate weakened, 4a not skipped |

---

## Risks

**The e2e stays red after the fix.** Then the frontend hop has its own defect and
Level 3 should have caught it. Handling: Level 3 runs *before* the fix precisely
so its colour is known independently. If Level 4 is red while 1–3 are green, the
run reports a second cause rather than patching blindly; that is a legitimate
retry-then-block path under ADR-0144.

**The integration test is red for the wrong reason.** A timeout is a weak signal
— an unbootable stack looks the same as a dropped frame. Mitigation: the failure
message names `ResolvedOverlayTextChangedWithoutFab` as the log line that
distinguishes them, and the test's own setup asserts the overlay is resolvable
over HTTP *before* it touches the hub, so a broken fixture fails earlier and
differently.

**The reverse index is in-memory.** A SystemVariables restart between publish and
value-set would empty it and the test would see no overlays. Handling: the wait
in step 2 is against the live snapshot endpoint, so a lost index shows up there
rather than as a mystery timeout later.

**Iteration 0 of the span carries warm-up.** #2014's shape. Not mitigated in
code; the report already prints every figure separately, and the verification
note reads the list rather than the median.

## What is explicitly not being built

A `V2` of the contract. A change to the guard. A dashboard. A fix for
`SystemVariableArchivedV1`. A constitution edit. A restored
`kiosk-label-follows-its-variable.spec.ts`.
