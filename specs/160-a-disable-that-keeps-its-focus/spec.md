# Spec 160 — A disable that keeps its focus

**Phase:** 1 (Specify) — ADR-0037
**Issue:** [#2387](https://github.com/smartsolutionslab/smart-sentinel-eye/issues/2387) · **Branch:** `fix/2387-a-disable-that-keeps-its-focus`
**Lane:** autonomous (ADR-0144). **Engineer:** `frontend-engineer` — all tasks, no backend, no infra.
**Base:** `develop` at `b7c8332b`. Every line and file reference below was re-read at that SHA.
**ADRs:** ADR-0077 (Radix + Tailwind; "zero accessibility debt" is the obligation
this spec discharges), ADR-0113 (two-layer optimistic concurrency — the `If-Match`
version US2 stops resubmitting), ADR-0075 (RTK Query — `currentData` / `isError`
semantics are the whole of US2's mechanism), ADR-0074 (two frontends; `management-web`
only), ADR-0119 (keyed on the error code, not the status), ADR-0139 (new behaviour
starts red), ADR-0109 (`[P]` marking), ADR-0037 (phases), ADR-0144 (lane).
**Constitution:** §Testing — **all three stories are behaviour-changing; every
phase-4a test is RED.** No characterisation obligation arises (§7).

**Latency budget (§IV): N/A.** No leg. `management-web` is the operator console.
Nothing here is on the `event arrival → overlay rendered` path: no kiosk, no SFU,
no composite, no overlay-state push, no presentation buffer. No cell of the §IV
table moves and no measurement is owed.

**New ADR needed: no.** §6 states why, and states the one rule this spec
deliberately does **not** write.

---

## 1. The three findings, each verified against the code at `b7c8332b`

The issue was filed at the end of spec 158 and two of its three claims were
verified during that spec's phase 6. They were **re-verified here from the
current tip**, not carried over — spec 158 has merged since and every line
number in #2387 has moved.

### 1.1 Finding 1 — a focused Save that disables loses its focus. Confirmed, and the issue's own nuance is the important half.

Both dialogs render Save natively disabled:

`apps/management-web/src/features/overlays/OverlayEditorDialog.tsx:314`

```tsx
disabled={isLoading || (isEdit && (currentChain === undefined || chainFetching))}
```

`apps/management-web/src/features/layouts/LayoutEditorDialog.tsx:422`

```tsx
disabled={isLoading || knownCameras.size === 0 || (isEdit && (currentChain === undefined || chainFetching))}
```

`isLoading` is the **mutation's** pending flag (`OverlayEditorDialog.tsx:95`,
`LayoutEditorDialog.tsx:93`). So the sequence #2387 describes is real but its
first step is earlier than the issue says:

| # | Moment | `isLoading` | `chainFetching` | Save | Focus |
|---|---|---|---|---|---|
| 1 | Operator clicks Save | false | false | enabled | **Save** |
| 2 | `editDraftOverlayRevision(...)` dispatched | **true** | false | **natively disabled** | **`<body>`** |
| 3 | 409 `OVERLAY_REVISION_STALE` returns | false | — | — | `<body>` |
| 4 | `invalidatesTags` starts the chain re-read | false | **true** | disabled | `<body>` |
| 5 | Re-read answers | false | false | enabled | `<body>` |

**Focus is destroyed at step 2, by the pre-existing `isLoading` term, before the
conflict exists.** The `chainFetching` term #2379 added does not cause the loss —
it extends the window (steps 4–5) during which focus cannot be returned. This
matters for the fix: an approach that only reworks the chain terms would leave
the dominant cause untouched.

**Radix does not rescue it.** `Dialog` (`apps/shared/src/ui/primitives/Dialog.tsx:18`)
renders `RadixDialog.Content`, whose `FocusScope` is `trapped`. Its two rescue
paths both decline this case, verified in the installed source
(`@radix-ui/react-focus-scope`, reached through `@radix-ui/react-dialog` **1.1.23**,
`dist/index.mjs:47-59`):

```js
handleFocusOut2 = function (event) {
  const relatedTarget = event.relatedTarget;
  if (relatedTarget === null) return;            // <- a disable-blur is exactly this
  ...
},
handleMutations2 = function (mutations) {
  const focusedElement = document.activeElement;
  if (focusedElement !== document.body) return;
  for (const mutation of mutations) {
    if (mutation.removedNodes.length > 0) focus(container);   // <- removals only
  }
};
```

A `focusout` caused by disabling the focused element carries `relatedTarget: null`,
so the first handler returns. The MutationObserver is registered
`{ childList: true, subtree: true }` — **no `attributes: true`** — so setting
`disabled` produces no record at all, and the second handler never runs for it.
Focus therefore reaches `<body>` and stays there. (If some *later* unrelated
removal inside the dialog fires `handleMutations` while `activeElement` is
`<body>`, focus lands on the dialog **container** instead. Neither destination is
Save, which is why §5's tests assert the element focus is *on*, never merely that
it is not on `<body>`.)

**The precedent is this repo's own, twice.**
`apps/shared/src/ui/composites/OverlayEditor.tsx:649-657` (spec 154) and
`apps/shared/src/ui/composites/ChainRecoveryNotice.tsx:31-33` (spec 156) both
chose `aria-disabled` over native `disabled` and both wrote down the same reason.

**Not a regression from #2379.** `LayoutEditorDialog` has carried `chainFetching`
since spec 153 and `isLoading` since the dialog existed.

### 1.2 Finding 2 — a refused re-read re-enables Save on a stale version. Confirmed at the installed RTK Query version.

The version installed is **`@reduxjs/toolkit` 2.12.0**
(`apps/management-web/node_modules/@reduxjs/toolkit/package.json`). Both citations
in #2387 land on the right lines at that version:

`dist/query/rtk-query.modern.mjs:1443-1455` — `queryThunk.rejected`:

```js
updateQuerySubstateIfExists(draft, arg.queryCacheKey, (substate) => {
  if (condition) {
  } else {
    if (substate.requestId !== requestId) return;
    substate.status = STATUS_REJECTED;
    substate.error = payload ?? error;
  }
});
```

Only `status` and `error` are written. **`data` is retained.**

`dist/query/react/rtk-query-react.modern.mjs:155` — `currentData: currentState.data`,
the raw substate `data` (the surrounding `queryStatePreSelector`, `:130-160`,
computes the separate `data` field; `currentData` bypasses it).

So after a refused re-read: `isFetching` false, `isError` true, `currentData`
still the pre-re-read version. Neither Save predicate consults `isError`. Both
dialogs already **bind** it — `isError: chainFailed` at `OverlayEditorDialog.tsx:91`
and `LayoutEditorDialog.tsx:89` — and both pass it only to `ChainRecoveryNotice`'s
`readFailed` prop (`:292` and `:378`). It appears in neither `disabled=`.

Save therefore re-enables, and a click sends `version: currentChain.version`
(`OverlayEditorDialog.tsx:194`, `LayoutEditorDialog.tsx:226`) — the version the
client already knows is stale — earning a **second identical 409**.

**Status code: 409 `Conflict`, not 412.** Verified in the source of truth for
both contexts:
`src/OverlayDesigner/Application/Commands/EditDraftRevisionErrors.cs:32-36`
(`OVERLAY_REVISION_STALE`, `HttpStatusCode.Conflict`) and
`src/LayoutComposition/Application/Commands/EditDraftRevisionErrors.cs:83-87`
(`LAYOUT_REVISION_STALE`, `HttpStatusCode.Conflict`), each carrying the comment
*"409 rather than 412 so it reads as the domain conflict it is"*.
**#2387 is right and #2379 was wrong** — and the miscitation survives in code:
`LayoutEditorDialog.tsx:406` and `:410` still say *"a stale-version 412"* and
*"a real `LAYOUT_REVISION_STALE` 412"*. FR-011 corrects them, because that comment
block is the one FR-002 rewrites anyway.

### 1.3 Finding 3 — the disable is unannounced on the dominant trigger. Confirmed.

`ChainRecoveryNotice` writes every announcement in exactly two places, and both
are reached only from an operator click:

- `activate()` (`ChainRecoveryNotice.tsx:147-167`) writes
  `Re-reading the {noun}…` at `:165`, immediately before `onReRead()`.
- The settle effect (`:88-132`) writes `The {noun} was read. Save is available.`
  at `:125-128` — but it returns at `:91` when `origin === null`.

`origin` is set only by `activate('retry')` / `activate('reload')` (`:152`), i.e.
only by a Retry or Reload **click**. An `invalidatesTags`-driven re-read leaves
`origin === null`, so:

- nothing is written on the way in,
- the settle effect returns at `:91` and writes nothing on the way out.

The live region at `:188-190` is permanently mounted and stays empty. So on the
**dominant** trigger a screen-reader operator hears the conflict alert
(`role="alert"`, `:237`), then Save silently becomes unavailable for the duration
of a network round trip, with nothing said and — per §1.1 — focus on `<body>`.

Spec 158's FR-004 declined a new affordance on the grounds that the status region
already explains the disabled Save. That justification is sound for the Retry and
Reload paths and **vacuous for the invalidation path**, which is the one that
fires on every conflict.

---

## 2. One issue or two — the call, made explicitly

**One issue. All three ship here.** #2387 leaves the split to whoever picks it up;
this is that decision, with the reasoning, so a reviewer can disagree with it on
the evidence.

The argument for splitting is real: findings 1 and 3 are accessibility, finding 2
is a correctness-of-reasoning defect, and they answer different questions. The
argument against is stronger on all three counts that matter here:

1. **They are one expression.** Finding 1's fix rewrites
   `OverlayEditorDialog.tsx:314` and `LayoutEditorDialog.tsx:422` — the exact
   lines finding 2 adds a term to. Split, the second issue reopens the same two
   lines days later, re-runs phase 6 on the same predicate, and conflicts with any
   parked branch touching them.
2. **Finding 2 needs finding 1's affordance to be safe.** Adding `chainFailed`
   to a *natively* disabled Save means Save disables on a refused re-read with
   focus on `<body>` and nothing announced — a dead end for a keyboard operator.
   With `aria-disabled` (US1) focus stays on Save, and the Retry arm is already
   on screen (`chainArmActive` is true when `readFailed`,
   `ChainRecoveryNotice.tsx:177`), so there is a route out. Shipping finding 2
   first would be a net accessibility regression.
3. **The test churn is shared.** §5.4 enumerates all seventeen existing assertions on Save's
   native `disabled` state across 7 files. They are rewritten once, not twice.

Each story below is nevertheless independently shippable and independently
testable, in the stated order, so the split is still available at the task
boundary if a reviewer wants it.

---

## 3. User stories, prioritised

### US1 (P1) — Save keeps its focus while it is unavailable

**As** an operator editing an overlay or a layout with a screen reader or a
keyboard, **when** my Save is refused for a stale version and the dialog re-reads,
**I want** my focus to stay on the Save button **so that** I can press it again
the moment it becomes available, without hunting for it.

Independently shippable: yes — the whole of US1 is the disable *mechanism* and
its guard. Independently observable: yes, §8.1.

### US2 (P2) — a refused re-read keeps Save unavailable

**As** the same operator, **when** the dialog's re-read of the current version is
itself refused, **I want** Save to stay unavailable **so that** I am not invited
to resubmit the version the system already told me was stale.

Independently shippable: yes — one term in each predicate, on top of US1.

### US3 (P3) — an unrequested re-read says so

**As** the same operator, **when** the dialog re-reads the version without my
asking it to, **I want** to hear that it is happening and that it finished
**so that** Save going unavailable and coming back is explained rather than
silent.

Independently shippable: yes — `ChainRecoveryNotice` plus one prop from each
dialog.

---

## 4. Functional requirements

### US1

- **FR-001** — Both Save buttons express unavailability with **`aria-disabled`**,
  never the native `disabled` attribute. The whole predicate moves, `isLoading`
  included: §1.1 step 2 shows `isLoading` is the dominant cause, and leaving one
  native term would leave one path that still blurs.
- **FR-002** — Each dialog computes the predicate **once**, into a single named
  local, used both by the button and by the submit guard. Today the predicate and
  `onSubmit`'s `currentChain === undefined` guard state overlapping conditions in
  two places (`OverlayEditorDialog.tsx:189` and `:314`).
- **FR-003** — **Submitting while the gate is closed does nothing.**
  `aria-disabled` keeps the element clickable *and* restores implicit form
  submission (with a natively disabled default button the browser suppresses
  Enter-to-submit; with `aria-disabled` it does not), so the guard is the only
  defence and must cover **both** routes:
  - clicking Save while blocked calls no mutation;
  - pressing Enter in a form field while blocked calls no mutation.

  The guard runs on the form's submit event **before** `handleSubmit`, so a
  blocked Enter does not even run validation. The existing
  `currentChain === undefined` guard inside the callback stays as
  defence-in-depth; its comment is updated, because "defensive rather than
  reachable through the UI" stops being true the moment the button is clickable.
- **FR-004** — Focus is **not moved** by this change. Nothing calls `.focus()`
  that did not before. FR-001 makes the loss not happen; it does not add a
  restoration.
- **FR-005** — The blocked button keeps today's visual treatment. The `Button`
  primitive's `disabled:opacity-50` no longer applies, so the call sites supply
  the `aria-disabled:` equivalent, mirroring
  `ChainRecoveryNotice.tsx:179`'s `aria-disabled:opacity-50 aria-disabled:cursor-progress`.
  **`pointer-events-none` is not added** — it would defeat FR-003's click test and
  `ChainRecoveryNotice` does not use it either.
- **FR-006** — The `Button` primitive (`apps/shared/src/ui/primitives/Button.tsx`)
  is **not** changed. See §6.

### US2

- **FR-007** — `chainFailed` joins the gate in both dialogs: a settled-but-refused
  chain read leaves Save unavailable. The term sits with the other chain terms,
  inside the `isEdit` guard, since the chain query is `skipToken` in create mode.
- **FR-008** — The operator is not left in a dead end: whenever FR-007 holds,
  `ChainRecoveryNotice`'s chain arm is on screen with Retry (`chainArmActive`
  is true when `readFailed`, `:177`), and a successful Retry already returns
  focus to Save via `onReadRecovered` (`:129-131`). This requirement is
  **asserted**, not assumed — §5.2.

### US3

- **FR-009** — `ChainRecoveryNotice` announces a re-read it did **not** start:
  - on the rising edge of `reReading` while `origin === null`, the same
    `Re-reading the {noun}…` text `activate()` writes;
  - on the falling edge, `The {noun} was read. Save is available.` on success,
    and a cleared status region on refusal (the chain arm's own
    `role="alert"` insertion is what announces a refusal — the rule spec 156's
    FR-006 already set, `ChainRecoveryNotice.tsx:95-107`).

  It does **not** call `onReadRecovered()` on that path: focus is already on Save
  (FR-001/FR-004), and moving it would steal focus from an operator who has moved
  on.
- **FR-010** — **The first read must stay silent.** A dialog opening also drives
  `reReading` false→true with `origin === null`; announcing there would say
  "Re-reading the overlay…" on every open. The discriminator is whether the chain
  has ever been read: a new boolean prop sourced from `currentChain !== undefined`
  in each dialog (suggested name `previouslyRead`). This is exactly spec 156's
  FR-007 trap (`ChainRecoveryNotice.tsx:71-78`) in a new place, and it is why
  US3 cannot be done inside the component alone.

### Cross-cutting

- **FR-011** — The two `412` miscitations in `LayoutEditorDialog.tsx:406` and
  `:410` become `409`, in the same comment block FR-002 rewrites. Comment only.
  §1.2 has the evidence.
- **FR-012** — No new ADR is written and no repo-wide rule is introduced. §6.

---

## 5. Acceptance scenarios (Gherkin)

Written against the dialog test harnesses that already exist
(`OverlayEditorDialogChainRecovery.test.tsx:31-114` and its layout twin) — a
`useSyncExternalStore`-backed fake chain query whose state can be stepped, and
`vi.fn()` mutation mocks. Two harness extensions are needed and are called out in
`tasks.md`: a **steppable `isLoading`** for the mutation (the current mock
hard-codes `isLoading: false`, `OverlayEditorDialogChainRecovery.test.tsx:74-79`)
and a **deferred** mutation promise.

### 5.1 US1 — happy path: focus survives the whole conflict cycle

```gherkin
Scenario: Save keeps focus from click through conflict to recovery
  Given the overlay edit dialog is open on a chain read at version 7
  And the Save button has DOM focus
  When the operator clicks Save
  And the edit mutation is pending
  Then Save is aria-disabled
  And document.activeElement is still the Save button
  When the mutation is refused with 409 OVERLAY_REVISION_STALE
  And the chain query steps to { data: v7, isError: false, isFetching: true }
  Then Save is aria-disabled
  And document.activeElement is still the Save button
  When the chain query steps to { data: v8, isError: false, isFetching: false }
  Then Save is not aria-disabled
  And document.activeElement is still the Save button
  When the operator clicks Save
  Then the edit mutation is called with version 8
```

The closing two lines are load-bearing: a gate stuck closed forever would satisfy
every assertion above them.

### 5.2 US2 — conflict path: a refused re-read keeps Save shut, with a way out

```gherkin
Scenario: the re-read is refused
  Given the overlay edit dialog is open on a chain read at version 7
  And the edit mutation has been refused with 409 OVERLAY_REVISION_STALE
  When the chain query steps to { data: v7, isError: true, isFetching: false }
  Then Save is aria-disabled
  And a Retry button is on screen
  When the operator clicks Save
  Then the edit mutation call count is unchanged
  When the operator clicks Retry and the re-read answers version 8
  Then Save is not aria-disabled
  And document.activeElement is the Save button
```

### 5.3 US1 — bad-request path: implicit submission while blocked

```gherkin
Scenario: Enter in a text field does not submit a blocked form
  Given the overlay edit dialog is open
  And the chain query is { data: v7, isError: false, isFetching: true }
  When the operator focuses the label text field and presses Enter
  Then the edit mutation is not called
```

This path exists **only after** FR-001: a natively disabled default button
suppresses implicit submission, `aria-disabled` does not. It is the single most
likely way for this change to introduce a defect worse than the one it fixes.

### 5.4 US1 — the existing assertions this change moves

Seventeen assertions and one doc comment read Save's **native** disabled state and
must be rewritten. They are enumerated here so phase 6 can check that none was
merely deleted, and so that ADR-0144's "may not weaken a gate" is visibly
satisfied. **Each rewrite is strictly stronger**: the attribute assertion *plus* a
behavioural one (a click while blocked calls no mutation; an available assertion
is followed by a click that does).

| File | Lines |
|---|---|
| `features/overlays/OverlayEditorDialog.test.tsx` | 297, 305 |
| `features/overlays/OverlayEditorDialogChainRecovery.test.tsx` | 235, 257, 500, 519 |
| `features/overlays/OverlayEditorDialogChainRetention.test.tsx` | 142, 168 |
| `features/overlays/OverlayEditorDialogResolvePreview.test.tsx` | 244 |
| `features/layouts/LayoutEditorDialogChainRecovery.test.tsx` | 211, 232, 476, 494 |
| `features/layouts/LayoutEditorDialogChainRetention.test.tsx` | 207, 332, 409 |
| `features/layouts/LayoutEditorDialogRetention.test.tsx` | 200 |
| `features/layouts/LayoutEditorDialogChainRecovery.test.tsx` (doc comment) | 433-441 |

**Rule for the engineer: a rewritten assertion may change its *mechanism* and
must not change its *claim*.** If an assertion cannot be restated as the same
claim about `aria-disabled` plus behaviour, that is evidence the behaviour moved
somewhere unintended — stop and report, do not adjust.

### 5.5 US3 — the announcement on the dominant trigger

```gherkin
Scenario: an invalidation-driven re-read announces itself
  Given the overlay edit dialog is open on a chain read at version 7
  And nothing has been clicked in the recovery notice
  When the chain query steps to { data: v7, isError: false, isFetching: true }
  Then the overlay-chain-recovery-status region reads "Re-reading the overlay…"
  When the chain query steps to { data: v8, isError: false, isFetching: false }
  Then the region reads "The overlay was read. Save is available."
  And document.activeElement is unchanged
```

### 5.6 US3 — the trap: a first read stays silent

```gherkin
Scenario: opening the dialog announces nothing
  Given the overlay edit dialog is closed
  And the chain query is { data: undefined, isError: false, isFetching: true }
  When the dialog opens
  Then the overlay-chain-recovery-status region is empty
  When the chain query steps to { data: v7, isError: false, isFetching: false }
  Then the region is still empty
```

### 5.7 Auth

**N/A, stated rather than skipped.** Nothing here reads, mints or scopes a token.
Both dialogs are already behind `management-web`'s OIDC-protected routes
(ADR-0080); no scope check, no fab authorization and no `Idempotency-Key`
surface changes. The mutations, their endpoints and their headers are untouched.

---

## 6. The ADR question, answered honestly

**No new ADR. The run is not blocked.** The reasoning, and its limit:

Choosing `aria-disabled` over native `disabled` for a control that can disable
while focused is **already decided in this repo, twice, with the reasoning
written down at the call site and shipped**:

- `apps/shared/src/ui/composites/OverlayEditor.tsx:649-657` — spec 154, Undo/Redo.
- `apps/shared/src/ui/composites/ChainRecoveryNotice.tsx:31-33` — spec 156, Retry
  and Reload.

Applying an existing, twice-shipped pattern to a third control is *applying* a
decision, not making one. ADR-0077 is the governing ADR and it already commits the
project to "zero accessibility debt" from the component layer.

**The thing that would be a new decision, and which this spec refuses to make:**
a repo-wide rule — *every control that can become unavailable while focused uses
`aria-disabled` and a handler guard* — together with its enforcement (an ESLint
rule, or `disabled:` classes removed from the `Button` primitive). That is
cross-cutting, it binds call sites nobody has looked at, and ADR-0144 forbids the
lane from writing it. Hence FR-006 (`Button.tsx` untouched) and FR-012.

**Recommendation to the human, not an action of this spec:** three instances is
where a pattern becomes a rule. Filing an ADR candidate for it is worth doing —
and it is a decision, so it needs a person.

**ADR-0149 (`Proposed`, open in PR #2391)** would eventually convert both of these
dialogs into routes. It changes nothing here: it is Proposed, nothing implements
it, and a route conversion is a straight lift of the same JSX. Noted so a later
reader does not mistake this spec for work done in ignorance of it.

---

## 7. Phase 4a colour declaration (constitution §Testing, ADR-0139)

**All three stories are behaviour-changing. Every phase-4a test is RED.**
There is no behaviour-preserving work item in this spec, so no characterisation
obligation arises. Stated positively rather than by omission, because ambiguity
resolves to red and a silent declaration is how that gets missed.

| Story | Behaviour | Colour | What "observed red" means concretely |
|---|---|---|---|
| **US1** | Changing — the disable mechanism changes, and the blocked-submit path is new | **RED** | The focus assertion fails on unmodified `develop`, because `document.activeElement` is `<body>` (or the dialog container), not the Save button. Quote the received value. |
| **US2** | Changing — Save is available on a refused re-read today, unavailable after | **RED** | `expect(save).toHaveAttribute('aria-disabled', 'true')` fails; and the click-calls-no-mutation assertion fails with a call count of 1. |
| **US3** | Changing — nothing is announced today on this path | **RED** | The status-region assertion fails against an **empty** region. Quote it: an empty-vs-expected diff is the proof the path was silent. |

**On the strength of the focus assertion**, since #2387 asks for it explicitly:
`expect(document.activeElement).not.toBe(document.body)` is **not acceptable** in
any of these tests. It passes against the container-refocus accident described in
§1.1 and against any future change that parks focus anywhere in the dialog. Every
focus assertion is `expect(document.activeElement).toBe(saveButton)`, against a
reference captured **before** the transition — the discipline
`OverlayEditorDialogChainRecovery.test.tsx:206` and `:233` already use.

**On `aria-disabled` being cosmetic**: no test may assert the attribute alone. An
implementation that renders `aria-disabled` and submits anyway would pass an
attribute-only suite while being strictly worse than `develop`. Every
gate-closed assertion is paired with a mutation-call-count assertion. The
existing file already makes this argument at
`OverlayEditorDialogChainRecovery.test.tsx:471` and `:510`.

---

## 8. Independent end-to-end test procedure (phase 5)

Manual, against the real Aspire stack, two operator sessions — the same setup
spec 158's phase 5 used to observe the 409 live. **Two browser profiles, one
overlay.**

### 8.1 US1 — the focus observation (the one that matters)

1. Boot the stack; open `management-web` in **profile A** and **profile B**,
   both on the same draft overlay revision.
2. In **B**, edit the label and Save. Confirm it succeeds.
3. In **A** — which now holds the stale version — open the editor, **tab to
   Save** (do not click it; a click would focus it anyway, but tabbing proves
   the keyboard path), and press **Enter**.
4. In the browser console, evaluate `document.activeElement` at four moments:
   while the PATCH is in flight, when the 409 banner appears, while the re-read
   GET is in flight, and after it answers.
   - **On `develop`:** `<body>` (or `div[role=dialog]`) from the first moment on.
   - **After this spec:** the Save button at all four.
5. Press **Enter** again immediately (while the gate is closed). Confirm the
   Network panel shows **no second PATCH**. Then wait for the re-read and press
   Enter once more: exactly one PATCH, carrying the new version.

### 8.2 US2 — the refused re-read

6. Repeat to the 409. Before the re-read GET completes, take the overlay API
   offline (stop the `OverlayDesigner` Api resource from the Aspire dashboard) so
   the re-read is refused.
7. Confirm Save is **aria-disabled** and the **Retry** control is on screen.
   Click Save: no PATCH in the Network panel.
8. Bring the resource back, click **Retry**, confirm the read succeeds, Save
   becomes available, and `document.activeElement` is the Save button.

### 8.3 US3 — the announcement

9. With a screen reader running (NVDA on Windows, or VoiceOver), repeat step 3.
   Confirm the spoken sequence: the conflict alert, then
   *"Re-reading the overlay…"*, then *"The overlay was read. Save is available."*
10. **The negative half:** close and reopen the dialog. Confirm **nothing** is
    spoken for the first read. This is FR-010, and it is the step most likely to
    be skipped.

Repeat 8.1 for the layout dialog. The layout path differs only in the noun and
in the extra `knownCameras` term.

---

## 9. Locked tech choices (no new dependency)

| Concern | Choice | ADR |
|---|---|---|
| App | `management-web` only; `kiosk-web` untouched | 0074 |
| Shared component | `apps/shared/src/ui/composites/ChainRecoveryNotice.tsx` (US3 only) | — |
| Data layer | RTK Query 2.12.0, existing hooks; no new endpoint, no `invalidatesTags` change | 0075 |
| UI | Radix `Dialog` + the existing `Button` primitive, unchanged; Tailwind `aria-disabled:` variants at the call site | 0077 |
| Forms | React Hook Form + Zod, existing schemas untouched | 0079 |
| Tests | Vitest + Testing Library + `userEvent`, the existing per-dialog harnesses | 0052 |
| Backend | none. No `src/` project changes, no migration, no contract, no message | — |

**Nothing is added to any `package.json`.**

## 10. Out of scope, named

- Any change to `Button.tsx`, and any repo-wide `aria-disabled` rule (§6).
- Any other disabled control in either app.
- Converting these dialogs to routes (ADR-0149 — Proposed, not accepted).
- Making the mutation itself idempotent (ADR-0142). A stale-version 409 is the
  correct answer and ADR-0113's server layer is not in question.
- Retry-on-conflict, in any form. ADR-0113 rules it out.

## 11. Assumptions, marked

- **A1.** The layout dialog's `knownCameras.size === 0` term joins the
  `aria-disabled` gate along with the rest, rather than staying native. Judgement,
  not evidence: one gate with two mechanisms is a bug waiting to be reintroduced.
  If a reviewer prefers it native, that is a one-line change and US1's tests do
  not depend on it.
- **A2.** `previouslyRead` is proposed as the FR-010 discriminator's name. The
  *source expression* (`currentChain !== undefined`) is required; the name is not.
- **A3.** Playwright 1.62's click actionability may or may not treat
  `aria-disabled="true"` as "not enabled". `e2e/overlays.spec.ts:145` clicks Save
  in edit mode with no explicit wait, so if it does not, that click can land on a
  closed gate and silently do nothing — a green e2e proving less than it did.
  Treated as **unverified and load-bearing**: `tasks.md` T009 makes the wait
  explicit rather than depending on the answer.
