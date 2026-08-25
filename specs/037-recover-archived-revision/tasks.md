# Tasks: A layout or overlay archived by mistake can be recovered

**Feature**: `037-recover-archived-revision` · **Spec**: [spec.md](./spec.md) · **Plan**: [plan.md](./plan.md)
**Issue**: 1877 *(written without a `#` deliberately — this repo's automation closes a merely-mentioned issue on merge)*

**26 tasks across five phases.** One line of domain change per aggregate, in a
feature that is not one line.

**The shape of the work is the shape of the guard.** The rule this feature
relaxes is written in **three layers per aggregate** — the domain method, the
application handler's own pre-check that runs *before* the domain is ever reached,
and the frontend's button gate. Move only the innermost and nothing changes
through the API. Move two and the app cannot reach it. The phases below are those
layers, in that order.

**Everything lands twice.** `Layout` and `Overlay` are deliberate twins
(ADR-0104), whose own intentional-pattern note says to check the sibling when the
lifecycle changes. Tasks are **paired per layer** rather than merged, because a
merged task is one an implementer can complete having done half of it. That
pairing is also what makes this feature unusually parallel — the two contexts
share no file.

**Nothing to add**: no migration, no new dependency, no new endpoint, no new
event, no new integration contract. If any of those proves necessary that is a
**finding to raise, not absorb**.

**Do not fix issue 1879 here.** A chain with a Published revision under an
abandoned draft offers no row actions at all — real, filed, and a different
change. T017 requires only that this feature's gate does not *misclassify* that
shape as recoverable.

**Do not extract anything shared between the twins.** ADR-0104's rule-of-three
revisit trigger needs a *third* revisioned aggregate. There isn't one.

---

## Phase 1: The decision

**Goal**: Write down what *archived* means before four files start assuming it.

- [x] T001 Write `docs/adr/0121-archived-is-out-of-service-not-unreachable.md`. It decides that an archived revision takes a chain **out of service** rather than out of reach, and that a chain with no Published and no Draft revision may branch from its newest Archived revision. It must reconcile with **ADR-0104** (the twins, and that the change lands in both — say so rather than leaving a reader to check) and with **ADR-0120**'s reasoning about terminal states. It must record the existing precedent that makes this a *reading* of the design rather than an overturning of it: `Revert` raises the archived event **without archiving anything**, purely to send kiosks away, so that event has always meant *stop showing this*, never *this is dead*

**Checkpoint**: the decision exists and nothing implements it yet.

---

## Phase 2: The domain

**Goal**: The aggregate accepts an archived source — and only in the one case.

**The fallback is narrow, and the narrowness is the feature.** *Branch from the
newest revision whatever its state* is shorter, reads equivalent, and is wrong: a
chain holding only a Draft would then branch, minting a **second competing
draft**. That is a worse defect than the one being fixed.

- [x] T002 [P] [US1] Add a private `NewestWhenFullyArchivedOrNull()` to `src/LayoutComposition/Domain/Layout/Layout.cs`, returning the highest-numbered revision **only when every revision is Archived** and `null` otherwise. **The condition lives inside the helper, not at the call site** — deliberately, so a later edit cannot widen it to "the newest revision" without deleting a named method that says what it is for
- [x] T003 [P] [US1] The same helper in `src/OverlayDesigner/Domain/Overlay/Overlay.cs`. Byte-identical in shape; the twins diverge only in their type names
- [x] T004 [US1] Use it in `Layout.BranchDraft` in `src/LayoutComposition/Domain/Layout/Layout.cs`: `CurrentPublishedOrNull() ?? NewestWhenFullyArchivedOrNull() ?? throw ...`. **Published wins whenever it exists**, whatever else the chain holds. Change the throw message — the old *"BranchDraft requires a currently-Published revision to copy from"* becomes wrong, because the only chains that now reach the throw are the ones with an open draft
- [x] T005 [US1] The same in `Overlay.BranchDraft` in `src/OverlayDesigner/Domain/Overlay/Overlay.cs`
- [x] T006 [US1] Leave `Revision.NewDraft`'s cloning of the grid and each tile **untouched** in `src/LayoutComposition/Domain/Layout/Revision.cs`, and the label clone in `src/OverlayDesigner/Domain/Overlay/Revision.cs`. Its comment explains that reusing the instances makes EF see one owned entity under two owners and throw on save. It was written for the published-source case and is what makes **FR-002** hold for the archived one. This task is *verify and leave alone* — if the recovery path seems to need it changed, that is a finding

**Checkpoint**: the domain recovers, and nothing observable through the API does.

---

## Phase 3: The application

**Goal**: The refusal narrows, says why, and closes the hole the narrowing opens.

The handler refuses **before** the domain is reached, so Phase 2 alone changes
nothing a caller can see. Each shape's required answer is in
[contracts/branch-draft-refusals.md](./contracts/branch-draft-refusals.md).

- [x] T007 [P] [US1] Narrow the pre-check in `src/LayoutComposition/Application/Commands/Handlers/BranchDraftRevisionCommandHandler.cs` from *no Published revision* to *no Published revision **and** an open Draft*. A fully-archived chain must fall through to the domain
- [x] T008 [P] [US1] The same in `src/OverlayDesigner/Application/Commands/Handlers/BranchDraftRevisionCommandHandler.cs`
- [x] T009 [P] [US2] **FR-007's message** in `src/LayoutComposition/Application/Commands/BranchDraftRevisionErrors.cs`: keep the `LAYOUT_NO_PUBLISHED_REVISION` code and the `409`, and change the message to name the open draft and its number. **The code stays deliberately** — the condition it names is still true and a client may switch on it, so changing it would be a breaking change bought for nothing. The message changes because an operator who now knows *some* chains without a Published revision can be branched is left nowhere by *"has no Published revision to branch from"*
- [x] T010 [P] [US2] The same in `src/OverlayDesigner/Application/Commands/BranchDraftRevisionErrors.cs`
- [x] T011 [P] [US1] **FR-009's failure** in `src/LayoutComposition/Application/Commands/BranchDraftRevisionErrors.cs`: a new `BranchDraftRevisionError` record at `409` reusing the **existing code string** `LAYOUT_NAME_TAKEN` from `CreateLayoutDraftErrors.cs` — the same condition reached by a different route, so a client already handling the create-path collision handles this one free. The *record* is new because the hierarchy is closed and generics are invariant; construct it through `BranchDraftRevisionFailures`, not the variant
- [x] T012 [P] [US1] The same in `src/OverlayDesigner/Application/Commands/BranchDraftRevisionErrors.cs`, reusing `OVERLAY_NAME_TAKEN`
- [x] T013 [US1] **FR-009's check, inside the recovery branch only**, in `src/LayoutComposition/Application/Commands/Handlers/BranchDraftRevisionCommandHandler.cs`, calling the existing `GetByNameAsync(fab, name)` with the recovering chain's own `Fab`. **No `excluding` parameter** — a fully-archived chain is excluded from its own lookup by the repository's predicate, so any hit is necessarily a different chain. **Comment why it sits inside the branch**: hoisted onto the published path it would match a live chain against itself and refuse every branch. That is a correctness condition, not an optimisation, and the code does not show it
- [x] T014 [US1] The same in `src/OverlayDesigner/Application/Commands/Handlers/BranchDraftRevisionCommandHandler.cs`, using the **global** `GetByNameAsync(name)` — overlay names carry no fab. Not a divergence from the twin: the twins are faithfully reflecting a difference their name rules already have

**Checkpoint**: recovery works through the API, and the app cannot reach it.

---

## Phase 4: The app

**Goal**: The door is visible, and the confirmation stops lying about it.

- [x] T015 [P] [US4] Gate the **Edit (new draft)** action on `chain.revisions.every(r => r.state === 'Archived')` **in addition to** the existing `newest.state === 'Published'` branch, in `apps/management-web/src/features/layouts/LayoutsPage.tsx`. **Not `newest.state === 'Archived'`** — that is unsound, because a chain can hold a Published revision under an abandoned newer draft
- [x] T016 [P] [US4] The same in `apps/management-web/src/features/overlays/OverlaysPage.tsx`, whose Edit button calls `branchDraft` directly with no designer step
- [x] T017 [US4] Rename `onEdit`'s second parameter from `published` to what it is — the revision the branch copies — in `apps/management-web/src/features/layouts/LayoutsPage.tsx`, and update its doc comment. **The call site already passes `newest`** and always has; only the name claimed otherwise. No new dialog, no new prop, no second code path. `LayoutEditorDialog` needs nothing: it re-reads the chain's current version after the branch rather than inferring it, so it is already indifferent to the source
- [x] T018 [P] [US3] Replace the layout's archive confirmation body in `apps/management-web/src/features/layouts/LayoutsPage.tsx` with the sentences given verbatim in [contracts/archive-confirmations.md](./contracts/archive-confirmations.md). The removed claim is *"this layout can never be edited or published again"*. **The replacement must not be "This cannot be undone"** — that is now false in the other direction, and an overstated warning is one operators learn to click through. Keep the kiosk paragraph and its `Published` condition exactly as they are
- [x] T019 [P] [US3] The same for the overlay in `apps/management-web/src/features/overlays/OverlaysPage.tsx` — *label is kept* rather than *tiles are kept*, and its own kiosk sentence unchanged

**Checkpoint**: an operator can recover a wall, and was told they could before they archived it.

---

## Phase 5: Evidence

- [x] T020 [P] [US1] **Recovery in the domain, asserting the payload**, in `tests/LayoutComposition.Domain.Tests/Layout/LayoutTests.cs` and `tests/OverlayDesigner.Domain.Tests/Overlay/OverlayTests.cs`. Publish, archive, branch — then assert the new draft carries the archived revision's **grid and every tile including the overlay binding** (layout) and its **label** (overlay), and is numbered max+1. Assert the payload, not that a draft exists: a fallback that branches an empty draft passes any assertion that a draft appeared, and the payload is the entire point of the feature
- [x] T021 [P] [US1] **Published still wins, asserted on the source**, in the same two files: a chain holding a Published revision **and** an archived newer one branches from the **Published** one. Assert *which revision was copied*, not that the branch succeeded — this is the case a naive "newest revision" fallback breaks in the opposite direction, and success alone is identical in both
- [x] T022 [P] [US1] **Recovery and FR-009 at the application layer**, in `tests/LayoutComposition.Application.Tests/Commands/BranchDraftRevisionCommandHandlerTests.cs` and `tests/OverlayDesigner.Application.Tests/Commands/BranchDraftRevisionCommandHandlerTests.cs`. Three assertions each: a fully-archived chain succeeds; a fully-archived chain whose name is held by another live chain returns the name-taken failure; **and a healthy published chain still succeeds**. The third is not padding — hoisting the name check out of the recovery branch makes the second pass and the third fail, so a task asserting only the refusal would ship exactly that bug
- [x] T023 [US2] **Leave the four SC-005 tests untouched, and prove it.** `LayoutTests.BranchDraft_without_a_Published_revision_throws`, `OverlayTests.BranchDraft_without_a_Published_revision_throws`, `BranchDraftRevisionCommandHandlerTests.Chain_without_a_Published_revision_returns_NoPublishedRevisionToBranchFrom` (LayoutComposition) and `BranchDraftRevisionCommandHandlerTests.Branching_without_a_Published_revision_returns_NoPublishedRevisionToBranchFrom` (OverlayDesigner). All four build on draft-only chains, which this feature deliberately leaves refused. **This task is "run them and show an empty diff", not "write".** If implementation finds itself editing one, the fallback was widened — that is a **finding to raise, not a fix to apply**
- [x] T024 [US1] **Recovery end to end over real SQL**, in `tests/Integration.Tests/LayoutComposition/LayoutLifecycleIntegrationTests.cs` and `tests/Integration.Tests/OverlayDesigner/OverlayRevisionLifecycleIntegrationTests.cs`: publish → archive → **branch → edit → publish** through the API against the Aspire stack, asserting the recovered payload survived the round trip and the chain kept its identifier. **All three steps, not just the branch** — a draft nobody can publish is not a recovery. Required rather than optional: the recovered draft clones an archived revision's **EF-owned entities** under a new owner in the same change-tracker, which a hand-written fake models away by construction. Spec 033's `ValueComparer` was caught only this way. **Run them; do not defer to CI** — spec 028 shipped two integration tests that had never executed
- [x] T025 [US3] **Rewrite the two frontend tests that cannot pass**, and add the gate tests, in `apps/management-web/src/features/layouts/LayoutsPage.test.tsx` and `apps/management-web/src/features/overlays/OverlaysPage.test.tsx`.
  - *Says the layout can never be edited or published again* and its overlay twin are spec 036's T014. **Rewrite them; do not delete them.** They must assert the new claim — *tiles are kept* / *label is kept*, and that it can be brought back — as specifically as they asserted the old one, **and assert the absence of both false sentences**. A test that merely stops asserting the old sentence passes against a page that still says it.
  - The **kiosk sentence in both directions in one test**: present for `Published`, absent for `Draft` (spec 036 FR-008). Asserting only the published case passes against a confirmation that always warns.
  - The **gate in both directions**: a fully-archived chain offers Edit, and a chain with a Published revision under an **abandoned archived draft** is not treated as recoverable. That second shape is issue 1879's; this asserts only that the gate does not misclassify it
- [x] T026 **The two deliberate breaks, then full verification.** An assertion that has never failed is a claim, not a check.
  - **(a)** Soften the layout confirmation in `apps/management-web/src/features/layouts/LayoutsPage.tsx` to exactly `This cannot be undone.`, run the page's tests, record which assertions go red and how many, revert. Spec 036 T018's discipline.
  - **(b) The important one, and unique to this feature.** Widen `NewestWhenFullyArchivedOrNull()` in `src/LayoutComposition/Domain/Layout/Layout.cs` and `src/OverlayDesigner/Domain/Overlay/Overlay.cs` to return the newest revision unconditionally. Record which of the four SC-005 tests go red. Revert. This is the evidence the narrowing is load-bearing rather than decorative, and it is the single most likely regression this feature can suffer later.

    > **Ran, and it corrected this task.** The task predicted all four would fail,
    > *"in the domain **and** in the application layer, in **both** twins"*. Only
    > **two** do — the domain test in each twin (LayoutComposition 128/129,
    > OverlayDesigner 64/65). Both application suites stayed fully green (76/76
    > and 41/41).
    >
    > The reason is the layering this whole feature is about: the handler's own
    > `openDraft is not null` check refuses a draft-only chain *before* the
    > domain is reached, so how wide the domain helper is never comes up there.
    > The two layers are independent guards rather than one guard tested twice.
    >
    > Which cuts both ways, and the second way is worth saying: a domain widening
    > **would not be visible through the API**, because the handler catches it
    > first. It would sit there as a latent divergence between what the aggregate
    > permits and what its only caller allows — caught by exactly two tests, not
    > four. That is the real reason those two matter.
  - Then `dotnet build -c Release` with analyzers clean, the affected test projects, `pwsh scripts/coverage-check.ps1`, `pnpm typecheck && pnpm lint && pnpm test`, and the Playwright suite. Verification note on the PR per [quickstart.md](./quickstart.md), covering **both twins** for every behavioural claim (SC-007)

---

## Dependencies

```
T001 (ADR-0121)
  │
  ├─▶ T002 ─▶ T004 ─┐        (layout domain)
  ├─▶ T003 ─▶ T005 ─┤        (overlay domain)
  │            T006 ─┤        (verify the clone, both)
  │                  ▼
  ├─▶ T007, T009, T011 ─▶ T013 ─┐   (layout application)
  ├─▶ T008, T010, T012 ─▶ T014 ─┤   (overlay application)
  │                              ▼
  ├─▶ T015, T016 ─▶ T017        (the gate)
  └─▶ T018, T019                (the words)
                    │
                    ├──▶ T020, T021, T022, T023
                    ├──▶ T024
                    └──▶ T025
                            │
                            ▼
                          T026
```

**T013/T014 need their error records (T011/T012) and their narrowed pre-check
(T007/T008)** — the check has nothing to return and nowhere to sit otherwise.

**T023 has no code dependency at all** and can be run at any point after Phase 2.
Running it early is the cheapest possible check that the fallback stayed narrow.

---

## Parallel opportunities

- **The twins, at every layer.** LayoutComposition and OverlayDesigner share no
  file, so T002/T003, T004/T005, T007/T008, T009/T010, T011/T012, T013/T014 and
  T015/T016 are all genuinely concurrent. This is the most parallel work in the
  feature, and it is parallel *because* the tasks were not merged.
- **T009/T010 (the message) with T011/T012 (the new failure)** — same files, so
  one author per file, but independent of the handler work.
- **T018/T019 (the words) with T015/T016 (the gate)** — same files again; the
  words and the gate are independent changes that happen to share a page.
- **T020, T021, T022** — different assertions over different layers.
- **T024** needs the Aspire stack and is the long pole. Start it as soon as
  Phase 3 lands rather than saving it for last.

---

## Implementation strategy

**MVP is T014.** Once both handlers fall through for a fully-archived chain, the
recovery works through the API and SC-001 is met at the boundary that matters.
Phase 4 makes it reachable; Phase 5 is evidence.

**Do the ADR first.** T001 before any code — not ceremony. Four files are about to
encode a claim about what *archived* means, and writing that claim down once
beforehand is how they encode the same one.

**Do the domain before the application.** The reverse order tempts a fix in the
handler alone, which would work and would leave the aggregate refusing something
its own caller permits.

**Run T023 early and again at the end.** It is free, it needs nothing, and it is
the tripwire for the mistake this whole feature is one careless edit away from.

**Do T025 last among the tests, and read it twice.** It is the only place existing
tests are rewritten, and the tempting change is the wrong one.

---

## Three things most likely to go wrong

1. **The fallback gets widened to "the newest revision".** It is shorter, it reads
   equivalent, and it makes a draft-only chain mint a second competing draft — a
   worse defect than the one being fixed. The condition lives inside a named
   helper (T002/T003) so widening it means deleting a method that says what it is
   for; T023 fails loudly if it happens anyway; T026(b) proves those failures are
   real rather than assumed.

2. **FR-009's name check gets hoisted out of the recovery branch.** *Check the
   name on every branch* reads like the more thorough choice. It inverts the
   behaviour: a live chain matches its own name, so every branch of every healthy
   chain is refused. T013/T014 require the comment; T022's **third** assertion —
   the healthy published chain still succeeds — is what catches it, and is the
   reason that assertion is not padding.

3. **The confirmation is softened rather than replaced.** The old sentence has to
   go, and *"This cannot be undone"* is where it lands under any hurried edit —
   false in the other direction, and exactly the phrase spec 036 built T018 to
   prevent. The contract gives the replacement verbatim, T025 asserts the absence
   of **both** false sentences rather than only the presence of the true one, and
   T026(a) proves that assertion fires.
