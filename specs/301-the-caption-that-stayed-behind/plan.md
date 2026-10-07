# Plan 301: The caption that stayed behind

Implements `spec.md`. The precedents are spec 150, which made the payload an ordered
collection with dense ordinals, and spec 300, which made its members kinded elements
and cut `OverlayRevisionPublishedV3`. This plan changes **no domain model**. Paint order,
the aggregate, the migration history and the `OverlayDesigner` API are untouched. What
changes is how the wall **pairs** an element with its text (US1, US2), and how resolved
text names what it was resolved from (US2).

US1/US3 `file:line`s were written against the spec 300 tree (`e3671aef`). **US2 (PR-B,
#2720) citations were re-verified on `develop` at `0d1864f5`** (2026-10-07, after PR-A
#2717 merged); where a US2 citation below differs from the original, the corrected one is
the current one.

---

## Bounded contexts touched

| Context | What changes | Layers | Story |
|---|---|---|---|
| **Frontend: `kiosk-web`** | `Tile` holds the paired render set; template lookup; refetch-on-miss hook | — | US1, US2 |
| **Frontend: `apps/shared`** | `ResolvedOverlaySnapshot` and hub message types | — | US2 |
| **Shared.Contracts** | `ResolvedOverlayTextChangedV3` + `ResolvedOverlayTextV3`; V2 deleted | — | US2 |
| **SystemVariables** | Producers emit `(template, resolved)` pairs; snapshot DTO reshaped | Application | US2 |
| **LayoutComposition** | V3 handler; notification and hub message reshaped | Domain (notification record only), Application, Infrastructure | US2 |
| **AuditObservability** | `IntegrationEventAuditHandler` V3 overload; resource-map convention test | Application | US2 |
| **OverlayDesigner** | **Nothing.** Characterisation tests only | — | US3 |

No new Aspire resource, queue, container, scope or Keycloak change. **No infra engineer is
needed.** The boundary rule holds: SystemVariables and LayoutComposition meet only through
`Shared.Contracts` (NetArchTest).

---

## Why no ADR

Four things in this spec could look like decisions. None of them is a new one.

1. **Reordering** is not an operation. It is the existing wholesale `PATCH` (spec 150
   FR-006) with a permuted body. A `MoveElement` mutator *would* need an ADR, because it
   would revise ADR-0112 §2 (members have no identity). It is rejected (spec decision 2).
2. **Overlap is legal.** This ratifies spec 150 FR-004 and adds no rule.
3. **The resolved-text contract cut** bumps to `V<N+1>` because the payload's meaning
   shifts (index-aligned → template-keyed). **ADR-0073 is *not* the authority for deleting
   V2 in the same commit** — it says the opposite: `docs/adr/0073-event-versioning.md:25-27`
   requires the old version to stay publishable through a ~3-month deprecation window, and
   `:35-36` requires a later coordinated removal PR. An earlier draft of this plan cited it
   for the clean cut; that citation was wrong. The actual authority is precedent:
   - **ADR-0112 §3** ("Contract versioning + migration — a clean V2 cut", `:100-128`;
     "V1 is removed in the same feature… no dual-publish", `:109-111`): every subscriber
     is in-repo and pre-production, so a deprecation window is speculative generality;
   - **spec 150 FR-009**: this same event's own prior V1→V2 cut, made the same way with no
     ADR written;
   - **spec 300 / ADR-0165** (`:65`, "inside a clean `OverlayRevisionPublishedV3` cut").

   Spec 301 recorded the clean-cut decision through its own human gate (`spec.md:429`).
   **A human has additionally signed off on this exact exception for #2720, recorded on
   the issue**; that sign-off plus the precedent above is the basis for proceeding without
   a new ADR or an ADR-0073 amendment. Correcting ADR-0073's text to match the repo's
   actual pre-production practice in general is tracked separately as **#2738** and does
   not block this spec.
4. **Aging the paired set** is ADR-0129 §2 ("a label is aged to match its picture")
   applied to spec 150 FR-014's "the set ages as a unit". **Reviewer check:** ADR-0129
   speaks of the *label*, meaning the value. If the reviewer reads §2 as "only the value
   is aged", then US1 widens it. The right response is a one-paragraph amendment note on
   ADR-0129 stating that geometry ages with its text, not a new ADR. The architect's
   reading is that §2 was written when a label's geometry could not change while it was
   painted. It now can (a republish), and pairing it with its text is the only reading
   under which "the label describes the same moment as the picture" holds for both of
   its halves.

---

## US1: the hold carries the paired set (kiosk only)

### Today (`apps/kiosk-web/src/features/cell/LayoutGrid.tsx`)

```
elements (publishedOverlay, immediate)
liveTexts      = elements.map((el, i) => snapshot?.resolvedTexts[i] ?? (Text ? el.text : ''))   :438
stableLiveTexts = useStableByKey(liveTexts, liveTexts.join('\0'))                                   :453
resolvedTexts  = useLabelDelay(stableLiveTexts, frameAge, reportHeld)          // HELD              :469
renderElements = elements.map((el, i) => ({ ...el geometry (NOT held), text: resolvedTexts[i] ?? el.text }))  :471-494
```

Geometry is current and text is held, and the two are re-joined **by index**. A permutation
mis-joins them for the length of the hold. A Box/Text swap paints `""`, because
`"" ?? x` is `""`.

### After

```
liveElements   = elements.map((el, i) => pair(el, liveTextFor(el, i)))   // geometry + kind + colour + text, NOT held
liveKey        = liveElements.map(key).join('\0')    // key = kind|color|x|y|w|h|fontSizePx|text
stableLive     = useStableByKey(liveElements, liveKey)
heldElements   = useLabelDelay(stableLive, frameAge, reportHeld)          // the PAIRED set is held
renderElements = overlayUnavailable ? undefined : heldElements
```

- **`useLabelDelay` is not modified.** It is already generic over `T` and compares by
  reference (`useLabelDelay.ts`, "Generic over the held value"). `useStableByKey` provides
  the reference stability, exactly as it does for the text list today. Its fail-open rules
  carry over unchanged:
  - `undefined` is never held;
  - an unreadable or over-cap frame age paints at once;
  - one `onHeld` fires per hold.
- **`overlayUnavailable` bypasses the hold**, as it does today. Removal is not held
  (ADR-0129, `useLabelDelay`'s "Removal is not held either").
- **The key covers every painted field.** A field missing from the key makes a change to
  that field paint unheld, which reintroduces the mis-join for that field. The red test
  includes a geometry-only republish to catch exactly that.
- `renderElementsKey` (`:507`), the `overlay_draw` measurement key, is **not changed**. It
  stays `kind|color|text`. Adding geometry to it is follow-on 2. US1 must not change
  *when* `overlay_draw` is measured, only *what* is painted.
- `liveTextFor(el, i)` is pseudocode, not a named function. As shipped by PR-A it is the
  inline expression at `LayoutGrid.tsx:446`
  (`snapshot?.resolvedTexts[index] ?? (element.kind === 'Text' ? element.text : '')`)
  inside the `liveElements` map (`:445`). US2 replaces that expression with the template
  lookup. Keeping it positional in US1 is what made US1 shippable alone.

### Existing tests that may encode the defect

`CellPage.test.tsx` and the `useLabelDelay` / `LayoutGrid` tests were written when only
text was held. **Any existing assertion that a geometry change paints before the hold
elapses encodes the defect US1 fixes.** If one exists:
- it changes in the US1 commit;
- the PR lists it by name, with before and after quoted, as an *intended* behaviour change.

It must not be silently adjusted. This is the one place in this spec where an existing
assertion may move, and it may move only in that direction.

---

## US2: resolved text names its template

### Shared.Contracts

New file `src/Shared.Contracts/SystemVariables/ResolvedOverlayTextChangedV3.cs`:

```csharp
public sealed record ResolvedOverlayTextChangedV3(
    Guid Overlay,
    IReadOnlyList<ResolvedOverlayTextV3> Texts,
    long Version,
    EventMetadata Metadata) : IIntegrationEvent;

public sealed record ResolvedOverlayTextV3(string Template, string Resolved);
```

- **Delete `ResolvedOverlayTextChangedV2.cs` in the same commit.** Its tests move to a V3
  file.
- **The doc comment carries the pairing rule.** `Texts` holds one entry per distinct
  `Text` template, in ordinal order of first appearance. Shapes contribute nothing.
  Consumers pair by `Template`, **never by index**.
- **Pairs, not two parallel lists.** Two lists can disagree in length or order, which is
  the defect class this spec exists to remove.
- **Primitives only** (ADR-0040).

Before deleting V2, run `grep -rn "ResolvedOverlayTextChangedV2" src tests apps e2e`
and paste the output into the PR. The consumer list in §"Bounded contexts touched" was
built from that grep on `e3671aef`.

### SystemVariables

**`IReverseIndex` does not change.** It already caches each overlay's templates in ordinal
order, with `""` for shapes (`OverlayRevisionPublishedV3Handler`), and that is exactly the
input the pairs need.

- **New `Application/Resolution/ResolvedTextPairs.cs`** (or a static method on the existing
  resolver type, whichever reads better at the call sites). It is a pure function from
  `(IReadOnlyList<string> templates, IResolver, snapshot)` to an ordered list of
  `(template, resolved)`:
  - skips `""`;
  - drops a template already seen (`StringComparer.Ordinal`).
  - **Written once.** The three producers below call it, so the pairing rule cannot
    diverge between the push and the snapshot.
- **`VariableValueChangedDomainEventHandler`** (`:85`) and
  **`VariableArchivedDomainEventHandler`** (`:102`) build `ResolvedOverlayTextChangedV3`
  from it. The version bump, fab scoping and pre-commit ordering (#2426) are untouched.
- **`SystemVariableValueRequestedV1Handler`: answered, it does not emit.** It dispatches
  `SetVariableValueCommand` (`SystemVariableValueRequestedV1Handler.cs:83-98`), and the
  push comes from `VariableValueChangedDomainEventHandler`. Its production code changes
  only in the stale V2 comment at `:95`. **Its test does change:**
  `SystemVariableValueRequestedV1HandlerTests.cs:350-351` asserts on the published
  `ResolvedOverlayTextChangedV2` and migrates to V3 (type and accessor only).
- **Stale V2 / index-alignment doc comments, corrected in the same commit** (they compile
  either way, so nothing else will catch them):
  - `Application/Resolution/IOverlayTextVersions.cs:5` (names `ResolvedOverlayTextChangedV2.Version`);
  - `Infrastructure/SystemVariablesInfrastructureModule.cs:31` (names V2);
  - `VariableValueChangedDomainEventHandler.cs:17`, `VariableArchivedDomainEventHandler.cs:17` (cref V2);
  - `tests/SystemVariables.Infrastructure.Tests/Resolution/ReverseIndexSeederHostedServiceTests.cs:215`
    ("`ResolvedTexts` is index-aligned with the element array by contract"): comment
    wording only; its assertions are unedited.
- **`GetOverlaySnapshotQueryHandler`:**
  - `ResolvedOverlaySnapshotDto(Guid OverlayIdentifier,
    IReadOnlyList<ResolvedOverlayTextDto> Texts, long Version)`, with
    `ResolvedOverlayTextDto(string Template, string Resolved)` in `Application/DTOs/`;
  - **#2426's version-before-text read order is preserved line for line.**

### LayoutComposition

- `ResolvedOverlayTextChangedV2Handler` → **`ResolvedOverlayTextChangedV3Handler`**. Same
  relay, now carrying pairs. Registration changes in
  `LayoutCompositionInfrastructureModule`. Destructure first: `var (overlay, texts,
  version, metadata) = message;`. Watch `HandlerDeconstructionTests`.
- **`ResolvedOverlayTextChangedNotification`**
  (`Domain/Layout/ILayoutLifecycleBroadcaster.cs:121`) changes `ResolvedTexts` to
  `IReadOnlyList<ResolvedOverlayText>`, a sibling `sealed record ResolvedOverlayText(string
  Template, string Resolved)` in the same file. Its neighbours are already primitive
  "wire shape" records in Domain (`OverlayLifecycleArchivedNotification`), so this follows
  them.
  - **Confirm at T011 that `PrimitiveBoundaryTests` excludes these notification
    records**, the way it evidently excludes the existing ones.
  - If it does not, carry the pairs as the existing records carry their primitives, and
    raise the finding. **Do not add a suppression** (CLAUDE.md, autonomous-lane
    prohibitions apply in spirit here).
- **The notification's doc comment is stale.** It claims a push when "the overlay itself
  is republished with new references", but `OverlayRevisionPublishedV3Handler` only
  upserts the index. Correct the sentence in the same commit, because this spec relies on
  there being **no** publish-time push.
- `ResolvedOverlayTextChangedHubMessage(Guid Overlay, string Fab,
  IReadOnlyList<ResolvedOverlayTextHubEntry> Texts, long Version)`, and
  `SignalRLayoutLifecycleBroadcaster` maps to it.

### AuditObservability

- Replace the `IntegrationEventAuditHandler` V2 overload (`IntegrationEventAuditHandler.cs:55`)
  with a V3 one.
- **The V2 resource mapping is a hand-tweak, not a convention.** It is registered
  explicitly at `V1ResourceMap.Conventions.cs:74`
  (`Add<ResolvedOverlayTextChangedV2>(map, DomainResourceKind.Overlay, changed => changed.Overlay)`).
  Edit that line to V3 directly; there is no convention to rely on.
- **Change the existing case 16** in `V1ResourceMapTests.cs:441-453`
  (`ResolvedOverlayTextChangedCase`) from V2 to V3: the type, and the constructor's
  `["ResolvedText-sentinel"]` becomes one `ResolvedOverlayTextV3` pair. **Do not add a new
  case.** The expected resource kind (`Overlay`) and resource id (`overlay.ToString()`)
  are unedited.

### Frontend (US2)

- **`apps/shared/src/api/systemVariables.api.ts`:** `ResolvedOverlaySnapshot = {
  overlayIdentifier; texts: { template: string; resolved: string }[]; version }`. Tags
  are unchanged.
- **`apps/shared/src/realtime/layoutHub.ts`:** `ResolvedOverlayTextChangedMessage = {
  overlay; fab; texts; version }`. Its doc comment (`:83-94`, which names
  `ResolvedOverlayTextChangedV2` at `:86` and describes the index alignment) is rewritten
  to the pair-by-template rule. Same for `systemVariables.api.ts:77-83`.
- **`useOverlayHubHandlers.ts`:** the `upsertQueryData` payload takes `texts`. **The fab
  filter, static-label report and version guard keep their order and logic** (FR-008).
  Their tests change only where they construct messages.
- **`LayoutGrid.tsx` `Tile`, the inline positional lookup at `:446`** (inside the
  `liveElements` map; "`liveTextFor`" below is pseudocode for that expression, not an
  existing function). Also update PR-A's US1 comment block at `:432-444`, which says the
  lookup "stays positional in US1":
  ```
  resolvedByTemplate = useMemo(() => new Map(snapshot?.texts.map(t => [t.template, t.resolved])), [snapshot])
  liveTextFor(el) = el.kind !== 'Text'            ? undefined
                  : !el.text.includes('{{')       ? el.text
                  : resolvedByTemplate.get(el.text) ?? el.text      // raw template on miss, never a neighbour's
  ```
  The `""` padding, and the positional `liveTexts` list, are deleted.
- **New hook `apps/kiosk-web/src/features/cell/useTemplateMissRefetch.ts`** with its own
  test file. Disjoint files, so it can be built in parallel with the type changes.
  - **Input:**
    - the overlay identifier;
    - a stable "publication key" for the rendered set: the joined templates of the
      published revision. It changes on every publish that changes text and on no other
      render;
    - whether any placeholder template is missing from `resolvedByTemplate`;
    - the query's `refetch`. **`Tile` does not destructure it today**: it reads only
      `{ data: snapshot }` from `useGetOverlaySnapshotQuery` (`LayoutGrid.tsx:427-430`).
      T015 adds `refetch` to that destructuring. Note the query is skipped when
      `overlayIdentifier` is null, there is no placeholder, or the fab is unnamed; the hook
      must not call `refetch` while the query is skipped.
  - **Behaviour:** on a miss, it calls `refetch` at 1 s, 2 s and 4 s, stopping early once
    the miss clears. After the third failed attempt, it logs
    `logResilienceEvent('hub', 'resolved-text-template-miss', { overlay })` once per
    publication key. It resets when the publication key changes. Timers are cleared on
    unmount and on key change.
  - **The 3 attempts / 1-2-4 s figures are an unmeasured guess**, chosen to cover a
    Wolverine consumer that is seconds behind without polling a down service forever. They
    are recorded as an assumption in the PR. **Not configuration** (ADR-0036: no knob
    without a need).

**Why not a server push on publish instead of a kiosk refetch:** SystemVariables resolves
per fab (ADR-0115/0145). A publish-time push would have to fan out one frame per fab that
holds any referenced variable. That is a new fan-out rule in the resolution loop, which is
on the event → overlay leg. A bounded refetch from the one kiosk that missed costs nothing
on that leg. It is also only needed for a genuinely new template, because template keying
already makes every reorder correct with no fetch at all.

---

## US3: characterisation of the reorder path (no production change)

- **Integration** (`tests/Integration.Tests/OverlayDesigner/OverlayRevisionLifecycleIntegrationTests.cs`).
  Add a method to the **existing** class, so no shard-filter entry is needed:
  1. create `[Box, Text, Ellipse]`;
  2. branch;
  3. `PATCH` `[Ellipse, Text, Box]` with `If-Match`;
  4. `GET` in that order;
  5. publish;
  6. capture `OverlayRevisionPublishedV3` and assert its `Elements` kinds are `[Ellipse,
     Text, Box]`. Use the existing capture helper the lifecycle tests already use for V3
     (spec 300 T013).
- **Concurrency.** Two reorders under the same `If-Match`: the second gets 409 and the
  first's order stands. If `OverlayLifecycleIntegrationTests` already asserts 409 for a
  stale wholesale replace, cite it and **do not duplicate it**. A reorder *is* a wholesale
  replace.
- **e2e**, new `e2e/kiosk-paints-a-reordered-overlay.spec.ts`:
  1. API-seed `[Box, Text "Zone A", Ellipse]`, publish and bind;
  2. read the overlay nodes' DOM order (`data-testid` plus `data-kind`);
  3. republish reordered and wait for the new order;
  4. assert `getComputedStyle(n).zIndex === 'auto'` for every overlay node.

  Seed through the API helpers per ADR-0162. **Check whether e2e has a shard list
  analogous to `ci-shards/shard-N.filter`, and add the entry if so.**
- **Expected green on the first run.** A red here means the ordering premise this spec
  rests on is false: **stop and report**. Do not fix it inside this spec.

---

## Phase 4a: colour per story

### RED: observed failing, output quoted in the PR

**US1** (Vitest, `kiosk-web`, a `Tile` / `LayoutGrid` render test with a controlled frame
age of 120 ms and fake timers):
- A two-caption reorder: during the hold, no rendered state pairs a caption's text with
  the other revision's geometry. **On unmodified code this must fail**, with the node at
  P showing L2's text. Quote the failure.
- `[Box, Text]` → `[Text, Box]`: no rendered state paints an empty caption. Must fail
  today, with the caption showing `""`.
- A move-only republish is held, and paints after the hold. Must fail today, because
  geometry paints at once.
- A held reorder reports exactly one hold.
- **The counterfactual for the key:** drop one geometry field from the stable key and
  show the move-only test turns red. Then restore it. This proves the key, not just the
  happy path (memory: prove a guard by counterfactual).

**US2:**
- `Shared.Contracts.Tests`: the V3 shape, and V2 absent (reflection over the assembly).
- `ResolvedTextPairs`: `["", "A {{x}}", "B {{x}}", "A {{x}}"]` → `[("A {{x}}", "A 7"),
  ("B {{x}}", "B 7")]`. Shapes and duplicates are dropped and order is kept.
- `VariableValueChangedDomainEventHandlerTests` / `VariableArchivedDomainEventHandlerTests`:
  the emitted V3 carries pairs, and there is still **exactly one event per overlay**.
- `GetOverlaySnapshotQueryHandlerTests`: the pairs, and the version read before the text
  (the #2426 assertion stays as written).
- `ResolvedOverlayTextChangedV3HandlerTests` (LayoutComposition): pairs reach the
  notification unchanged, and the fab is unchanged.
- **Kiosk, the premise test:** a tile holds the old revision's snapshot, a reorder is
  published, and the snapshot is **not** refreshed. Each caption must show its own
  template's value.
  - **Observe it red first on today's positional shape:** the test's fake server returns
    what SystemVariables returns for the old revision. **Quote that output.**
  - With the contract change, only the fake server's response builder changes shape. The
    assertion is not edited.
- **Kiosk:** a new placeholder template with no entry shows its raw template, never the
  previous caption's value.
- **`useTemplateMissRefetch`** (fake timers): refetches at 1, 2 and 4 s; stops when the
  miss clears; logs exactly one `resolved-text-template-miss` after the third attempt;
  resets on a new publication key; clears its timers on unmount.

### CHARACTERISATION: green before, unmodified after

| Guard | Rule |
|---|---|
| US3's integration method and e2e | Written first, observed green on unmodified code, unmodified after both PRs. |
| `A_label_set_written_out_of_physical_order_still_reads_back_in_ordinal_order` and the three `..._in_ordinal_order` siblings | Byte-identical. |
| `OverlayLabelCharacterisation`, `OverlayLabelParity`, `OverlayLabelNodeCountCharacterisation`, `overlayLabelStyle.test.ts` | Byte-identical. `CameraViewer` is not modified. |
| `OverlayEditor*` guards, `OverlayEditorDialog*` tests | Byte-identical (FR-010). |
| `useLabelDelay.test.ts` | Byte-identical. The hook is not modified. |
| Kiosk hub-handler tests (fab filter, version guard, static-label report; they live in `CellPage.test.tsx`, ~35 `resolvedTexts:` construction sites) | Only message-construction lines change (FR-008). An edited `expect` is a block. |
| `SystemVariables.Application.Tests/EventHandlers/VariableValueChangedPreCommitTests.cs:76,127` | Only the V2→V3 type in the `OfType<…>()` capture lines changes (FR-008). The pre-commit ordering assertions (#2426) are unedited. |
| `SystemVariables.Application.Tests/EventHandlers/SystemVariableValueRequestedV1HandlerTests.cs:350-351` | Only the V2→V3 type and payload accessor change. |
| `apps/kiosk-web/src/features/revocation/useLayoutLifecycle.test.tsx:63` | Only the message-construction line changes (`resolvedTexts: [...]` → `texts: [{ template, resolved }]`). |
| `apps/shared/src/api/systemVariables.api.test.ts:11` | Only the fake response body changes shape. |
| `apps/kiosk-web/src/features/cell/LayoutGridLabelPairing.test.tsx` (PR-A's own US1 test) | **Assertions byte-identical.** Fixture construction may change shape (snapshot `texts` pairs), and the comment wording at `:17` and `:312` (which describes `resolvedTexts` joined by index) is updated. Nothing else. |
| `ReverseIndexSeederHostedServiceTests.cs:215` | Comment wording only (index-aligned → template-keyed). Assertions unedited. |
| `ResolvedTextReachesItsFabTests`, `VersionSurvivesARestartTests`, `OverlaySnapshotReadiness` | Only the payload accessor changes (`ResolvedTexts[i]` → `Texts` lookup by template). The fab and version assertions are unedited. |

---

## PR split

- **PR-A: US1 + US3.** Kiosk-only production change plus characterisation. Owner:
  `frontend-engineer`. US3's integration test goes to `test-writer` / `backend-engineer`.
  This is the smallest vertical that removes the mis-join users can actually hit (on every
  reorder of an aged tile).
- **PR-B: US2.** Cut from `develop` after PR-A merges. **It edits the same `Tile` function
  in `LayoutGrid.tsx` as US1** (the inline lookup at `:446`), so it is serial, not stacked.
  **Tracked as #2720** (PR-A was #2348 / PR #2717). Both engineers work the **same branch
  and the same PR**, backend first, then frontend. Backend
  (`Shared.Contracts` → SystemVariables / LayoutComposition / AuditObservability) by
  `backend-engineer`; kiosk side by `frontend-engineer`.
  - Each commit must build on its own. The contract cut, the deletion of V2 and every
    consumer change together in one commit, or the solution does not compile in between.
    The kiosk change can follow in its own commit, because the hub carries JSON and the
    kiosk breaks only at runtime: **a runtime break is still a broken commit for
    bisect**. Keep the kiosk type change in the same commit as the hub message change.

---

## Boundary and house rules the reviewer should check

- **No cross-context reference.** LayoutComposition reads `ResolvedOverlayTextV3` only
  through `Shared.Contracts` (NetArchTest).
- **Handlers destructure first.** Watch `HandlerDeconstructionTests` (two same-typed
  `string` fields in `ResolvedOverlayTextV3`: a transposed `Template`/`Resolved` compiles
  cleanly).
- **Use `Ensure.That`, never `ThrowIfNull`** (ADR-0105). Collections take an explicit
  type and a collection expression.
- **No `z-index`, no `CameraViewer.tsx` diff, no `OverlayEditor*` diff, and no
  OverlayDesigner `src/` diff.**
- **No new configuration knob** for the retry figures.
- **Shard filters:** any *new* integration test class needs a `ci-shards/shard-N.filter`
  entry. The plan puts US3 in an existing class to avoid one, and US2's backend tests are
  unit tests.

---

## Assumptions, marked

1. **Premise (US1, US2):** the mis-join is derived from reading code. The red tests decide
   it.
2. **Retry figures (US2):** 3 attempts at 1 s, 2 s and 4 s are unmeasured.
3. **`PrimitiveBoundaryTests` scope (US2):** assumed to exclude LayoutComposition's
   notification records. Checked at T011.
4. **`SystemVariableValueRequestedV1Handler` (US2):** resolved, no longer an assumption. It
   does not emit; it routes through `SetVariableValueCommand` (`:83-98`). Only its test
   (`:350-351`) and a comment (`:95`) change.
5. **Line numbers:** US1/US3 from `e3671aef`; US2's re-verified on `develop` `0d1864f5`.
