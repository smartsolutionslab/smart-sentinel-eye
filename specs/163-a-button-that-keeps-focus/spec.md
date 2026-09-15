# Spec 163 — a Button that keeps focus

**Phase:** 1 (Specify) — ADR-0037
**Issue:** [#2399](https://github.com/smartsolutionslab/smart-sentinel-eye/issues/2399) · **Branch:** `feat/2399-a-button-that-keeps-focus`
**Lane:** autonomous (ADR-0144) — `#2399` carries `agent:ready`. **Engineer:** `frontend-engineer` · **Reviewer:** `frontend-reviewer`
**Base:** `origin/develop` @ `5ea913dc`, cut fresh — **not stacked** (memory: *spec branches cut from HEAD*).
**ADRs:** **ADR-0151** (the decision being implemented), ADR-0077 (Radix headless +
custom design system — ADR-0151 extends it), ADR-0078 (Tailwind tokens), ADR-0074
(two apps + `apps/shared`), ADR-0139 (rules that fail the build), ADR-0144 (the lane;
phase 4a's two colours), ADR-0052 / ADR-0053 / ADR-0054 (test stack, naming, data),
ADR-0108 (Playwright e2e gate), ADR-0109 (`[P]` marking), ADR-0037 (the workflow).
**Constitution:** §Testing (both obligations — a new-behaviour test never seen failing
does not satisfy the phase-4 gate; a behaviour-preserving change owes characterisation
captured green first); §IV (latency budget — **N/A**, below).

**Latency budget (§IV): N/A.** Nothing on the `event arrival → overlay rendered` path
is touched. The files in scope are the management console's two editor dialogs, the
shared `Button` primitive and documentation. `Button` is not rendered by `kiosk-web`
and appears on no leg of the §IV table; no cell moves and no measurement is owed.

**New ADR required for the slice as scoped: no.** ADR-0151 is Accepted and §3 of it
specifies exactly the prop this spec builds. One question the issue asks — whether a
shape exists that *couples* the guard to the prop — **does** have a candidate answer
that ADR-0151 did not decide. It is **escalated, not taken** (§4, `[DECISION
REQUIRED]`). The slice ships either way, so this is an escalation, not a BLOCK.

---

## 1. Premise verification — every claim in #2399 and ADR-0151, checked against the code

Memory: *verify the issue premise before planning*. Everything load-bearing holds. One
figure in ADR-0151 does **not**, and it is the kind of miscount this repository has had
to correct four times already (§II twice, the phase-3 board gate, §IV's leg table).

### 1.1 `Button.tsx` is as ADR-0151 §3 describes it — confirmed

`apps/shared/src/ui/primitives/Button.tsx`, 41 lines, on `5ea913dc`:

- Props are `ComponentPropsWithRef<'button'>` plus `variant?: ButtonVariant` and
  `asChild?: boolean`. There is **no** `unavailable` prop and no `disabled` handling of
  its own — `disabled` arrives through `...rest` and lands on the native element.
- The base class string ends `disabled:pointer-events-none disabled:opacity-50
  transition-colors`. Those two `disabled:` variants are exactly what ADR-0151 §3 calls
  out as "what make the wrong choice comfortable", and they are still there.
- `type` defaults to `'button'` unless `asChild` is set.
- Rendering is `clsx(base, variants[variant], className)` — caller classes last.
- **There is no `Button.test.tsx`.** `apps/shared/src/ui/primitives/` contains
  `Button.tsx`, `ConfirmDialog.tsx`, `ConfirmDialog.test.tsx`, `Dialog.tsx`,
  `Input.tsx`, `Tooltip.tsx`, `index.ts`. The primitive with the most call sites in the
  product has no test file at all.
- `index.ts` is `export {};` — a comment and nothing else. Call sites import the deep
  path `@smart-sentinel-eye/shared/ui/primitives/Button`, so the barrel is not in play.

### 1.2 The three reference implementations — confirmed, and two of them do not use `Button`

This matters and neither the issue nor the ADR says it.

| Spec | Control | Element | Guard | File:line |
|---|---|---|---|---|
| 154 | Undo / Redo | **bare `<button>`** | `undo()`/`redo()` already return `false` | `OverlayEditor.tsx:658-677` |
| 156 | Retry / Reload | **bare `<button>`** | `if (reReading) return;` in `activate()` | `ChainRecoveryNotice.tsx:265-268`, `:348-377` |
| 160 | Save ×2 | **`Button` primitive** | the form's `onSubmit` (`handleFormSubmit`) | `OverlayEditorDialog.tsx:277-285`, `:356-363`; `LayoutEditorDialog.tsx` equivalents |

`OverlayEditor`'s Undo/Redo are unstyled `<button>` elements inside a flex div;
`ChainRecoveryNotice`'s Retry/Reload are inline `<button className="underline …">`
inside a `<p>`. Neither imports `Button` (the earlier grep hit on
`ChainRecoveryNotice.tsx` is `retryButtonRef` / `HTMLButtonElement`, not the
primitive). Converting them to `Button` would be a visual change to four controls, not
a prop adoption — **out of scope**, see §3.

Only the **two Save buttons** are `Button` call sites, and they hand-roll exactly what
the new prop is for:

```tsx
<Button
  ref={saveRef}
  type="submit"
  aria-disabled={saveBlocked}
  className="aria-disabled:opacity-50 aria-disabled:cursor-progress"
>
```

### 1.3 The population — re-measured, and ADR-0151's figure is a double-count

ADR-0151 "The population" says, measured at `36796e84`:

> - **32** `disabled={…}` sites across **17** `.tsx` files in `apps/`.
> - **6** production `aria-disabled={…}` sites across **4** files.

and §4 then says "The **32 native** sites stay as they are."

Re-measured on `5ea913dc` (today's `origin/develop`) and, for comparison, at
`36796e84` itself:

```sh
# every spelling, both SHAs — identical
git grep -noE '(aria-)?disabled=' <sha> -- 'apps/**/*.tsx' | awk -F: '{print $NF}' | sort | uniq -c
#   6 aria-disabled=
#  26 disabled=
git grep -lE 'disabled=' <sha> -- 'apps/**/*.tsx' | wc -l                      # 17
git grep -lE '(^|[^-])disabled=' <sha> -- 'apps/**/*.tsx' | grep -v '\.test\.' | wc -l   # 13
```

**The tree has not moved. The arithmetic was wrong when it was written.**
`26 + 6 = 32` and `13 + 4 = 17`: the ADR's "32 across 17" is the total of *both*
spellings, and its "6 across 4" then counts the `aria-disabled` half a second time. The
substring `disabled=` matches `aria-disabled=`, which is how it happened.

**Corrected population, `5ea913dc`:**

| Spelling | Sites | Files |
|---|---|---|
| Native `disabled={…}` (production `.tsx`) | **26** | **13** |
| Production `aria-disabled={…}` | **6** | **4** |
| **Total** | **32** | **17** |

No file carries both spellings (13 + 4 = 17 with no overlap). Native sites in test
files: **0**.

The 13 native files: `AuditPage`, `CamerasPage`, `EditCameraAddressDialog`,
`RegisterCameraDialog`, `RenameCameraDialog`, `LayoutsPage`, `OverlaysPage`,
`DryRunPanel`, `RuleDialog`, `SystemVariableDialog`, `SystemVariablesPage`,
`BackdropControls`, `ConfirmDialog`.

**Nothing in ADR-0151's decision turns on 26 versus 32** — "do not sweep" is argued on
the implicit-submission trap, not on the count. But a wrong number in a Decision
section is a number the next reader will quote. FR-008 corrects it, in the Context
*and* in §4, changing no decision text. That is a factual correction, not an amendment,
and the lane's "may not write an ADR" prohibition is about deciding, not arithmetic.

### 1.4 The submit trap is real, and it is **already covered** — confirmed

`e2e/overlays.spec.ts:426-451` is spec 160's implicit-submission test. It is the
genuine article, and it is the only one in the repository:

```ts
const patchRequestCountBeforeEnter = patchRequestCount;
await pageTwo.getByTestId('overlay-editor-text').press('Enter');   // a TEXT FIELD
await expect(saveButtonTwo).toHaveAttribute('aria-disabled', 'true');
await saveButtonTwo.focus();
…
expect(patchRequestCount).toBe(patchRequestCountBeforeEnter);      // re-asserted after a real settle
```

Real browser, Enter in `overlay-editor-text` (not on Save), counted through a
Playwright route on the PATCH, and — per spec 160 and ADR-0151's Implementation Notes —
proven to discriminate by disabling the guard and watching `Expected: 1, Received: 2`.
The count is deliberately re-asserted *after* the conflict alert appears, i.e. after a
forced wait of at least the route's one-second hold, so it cannot pass by running too
early.

`e2e/layouts.spec.ts` has no equivalent. That is a pre-existing gap and **this spec does
not close it** — see §4 Q2.

### 1.5 `aria-disabled="false"` is asserted, not merely `"true"` — the preservation trap

Eight existing assertions across five suites assert the attribute's **`'false'`**
value, not its absence:

```
OverlayEditorDialogChainRecovery.test.tsx:237,532   LayoutEditorDialogChainRecovery.test.tsx:214,508
OverlayEditorDialogChainRetention.test.tsx:148      LayoutEditorDialogRetention.test.tsx:210
OverlayEditorDialogResolvePreview.test.tsx:246
```

React stringifies boolean `aria-*` values, so `aria-disabled={false}` renders
`aria-disabled="false"`. Any implementation of `unavailable` that emits the attribute
**only when true** (`unavailable || undefined`, or a conditional spread) breaks all
eight. FR-002 pins this.

### 1.6 `ChainRecoveryNotice` uses `aria-disabled:cursor-progress` too

`controlClassName = 'underline aria-disabled:opacity-50 aria-disabled:cursor-progress'`.
So `cursor-progress` is the *pending-action* idiom at all three current sites, not a
generic unavailability idiom. FR-003 keeps it at the call site rather than folding it
into the prop, because "unavailable" and "busy" are different claims and only one of
them is what ADR-0151 §1 is about.

---

## 2. User stories

### US1 (P1) — the sanctioned path is the easy one

**As** the next author adding a control that disables itself when activated,
**I want** a named `Button` prop that does the `aria-disabled` + dimming correctly,
**so that** I do not rediscover the blur-on-disable browser behaviour for a fourth
time, and so that the prop's own doc comment tells me the guard is my job.

*Independently shippable:* yes. The prop plus its test plus its doc comment is a
complete, reviewable, useful change on its own.

### US2 (P1) — the prop has a real consumer

**As** a reviewer,
**I want** the two existing `Button` call sites that hand-roll `aria-disabled` to use
the prop,
**so that** the prop is exercised by production code rather than by its own test only,
and so that a future change to the dimming reaches every site at once.

*Independently shippable:* yes, but only **after** US1. A prop with zero call sites is
hard to review and easy to get subtly wrong (issue #2399's own words), so these ship
together in one PR.

### US3 (P2) — the record matches the tree

**As** the next person to read ADR-0151,
**I want** its population figure to be the one the repository actually has,
**so that** "the 32 native sites" is not quoted at a codebase that has 26.

---

## 3. Scope

### In scope

1. `unavailable` prop on `apps/shared/src/ui/primitives/Button.tsx`, with the doc
   comment ADR-0151's Implementation Notes require ("Put it where the next author will
   be — the prop's own doc comment").
2. A new `apps/shared/src/ui/primitives/Button.test.tsx`.
3. Adoption at the **two** `Button` call sites that already hand-roll the pattern:
   `OverlayEditorDialog.tsx` Save, `LayoutEditorDialog.tsx` Save.
4. Characterisation assertions pinning the class list and the `'false'` value at those
   two sites, captured green **before** the conversion.
5. ADR-0151 population correction (§1.3).

### Explicitly out of scope

- **No sweep.** The 26 native sites stay native (ADR-0151 §4). None is touched.
- **No lint rule.** Rejected by ADR-0151 as actively harmful.
- **The four bare-`<button>` `aria-disabled` sites are not converted** (§1.2). Moving
  `OverlayEditor`'s Undo/Redo or `ChainRecoveryNotice`'s Retry/Reload onto `Button`
  changes four controls' appearance (padding, rounding, variant colours, the `underline`
  inline treatment) for no behavioural gain. That is a visual redesign wearing a
  refactor's clothes, and it would put a behaviour-*changing* item into a slice whose
  second half must be characterised green.
- **No new Playwright test.** §4 Q2 argues this.
- **No `Button` barrel export.** `index.ts` stays `export {};` — call sites use the
  deep path and changing that is unrelated churn.
- **No `disabled`/`unavailable` mutual-exclusion type.** §4 Q1.

---

## 4. The four hard questions

### Q1 — Is there a shape that *couples* the guard to the prop? `[DECISION REQUIRED]`

**Short answer: not at compile time, and the one runtime shape that works is a
decision ADR-0151 did not make. It is escalated, not built.**

**Compile time: no such shape exists, and the reason is not a TypeScript limitation
worth working around.** The guard is a statement *inside* a handler body
(`if (reReading) return;`), or the *absence* of an effect
(`undo()` returned `false`), or a handler on a *different element* (the form's
`onSubmit`). No type can require any of those. The nearest candidates and why each
fails:

- *Require `onClick: (e) => 'refused' | 'acted'`.* Nothing stops a handler returning
  `'refused'` after acting. It types a promise, not a guard. It also cannot reach the
  form-`onSubmit` shape at all, which is the one shape that carries the real trap.
- *A branded `GuardedHandler<T>` the caller must construct.* The brand would be
  applied by an assertion the caller writes, so it asserts its own premise. This is
  the shape memory calls *guards that read the design artefact*.
- *Union-typed props making `unavailable` and `disabled` mutually exclusive.* This
  one **is** expressible and does compile-enforce something — but what it enforces is
  "do not pass both", which is not the guard. It also breaks any call site that
  spreads a props object into `Button`. Rejected as cost without the benefit; the doc
  comment says it instead.

**Dev-time assertion: no shape can observe the guard either.** `Button` can reach its
own form (`ref.current.form`), but React attaches `onSubmit` at the root container, so
`form.onsubmit` is `null` whether or not a handler exists. There is nothing to assert
against.

**Runtime: one shape does work, and that is the problem.** If `unavailable` made
`Button` swallow the caller's `onClick` and call `event.preventDefault()`, it would
refuse both routes in a real browser — the click route because the handler never runs,
and the implicit-submission route because the HTML implicit-submission algorithm fires
a `click` at the form's default button, which `preventDefault` cancels. That is a
genuine coupling, not a fake one.

Three reasons it is **not** built here:

1. **It exceeds ADR-0151 §3 as accepted.** §3 decides a prop that "emits `aria-disabled`
   plus the dimming classes". A prop that also intercepts activation is a different
   thing: the prop stops being a declaration and becomes a behaviour. Spec 161 hit the
   same boundary and escalated rather than deciding; the brief for this spec says that
   was the right call.
2. **It would silently neutralise spec 160's counterfactual.** The proof that the form
   guard in `OverlayEditorDialog.tsx` works is "delete it and watch the PATCH count go
   1 → 2". With `Button` preventing the default too, deleting the form guard would leave
   the e2e green. A change that makes an existing proof stop discriminating is worse
   than no change, and it is precisely the failure mode spec 162 was written about.
3. **It would still not be sufficient**, so it would be a false sense of completion.
   `preventDefault` on a click does not stop an `onClick` that this `Button` *does*
   call in the non-unavailable case from being wrong, does not cover
   `form.requestSubmit()`, and only covers implicit submission when this button is the
   form's *default* (first-in-tree-order) submit button. ADR-0151 §2's form guard would
   remain mandatory while looking optional.

**So, plainly, as the issue asks:** ship the prop as ADR-0151 §3 decided, and say in
its doc comment that the guard is the caller's and is not optional. The prop makes the
right thing easy; nothing available makes the wrong thing impossible. The runtime
coupling above is recorded here as a `[DECISION REQUIRED]` for a follow-up ADR if the
team wants it — with reason 2 as the cost that has to be paid for it.

### Q2 — What does this spec owe on the submit-button axis?

**It owes the existing coverage passing unmodified, and it owes no new Playwright
test. Here is why that is not a dodge.**

The prop *does* ship on two submit buttons (US2), so the axis is live. But:

- **The form guards are not touched.** `handleFormSubmit` in both dialogs is
  untouched by this slice — the conversion replaces `aria-disabled={saveBlocked}` +
  `className` with `unavailable={saveBlocked}` + a shorter `className`, and nothing
  else. `saveBlocked` itself is untouched. The trap was opened by spec 160 and closed
  by spec 160 in the same change; this spec neither re-opens nor re-closes it.
- **The real test already exists and already discriminated** (§1.4). Writing a second
  Playwright test pressing Enter in the same text field of the same dialog would not
  be new coverage; it would be a second copy of a proof, at the cost of another
  full-stack e2e run. ADR-0108's gate is satisfied by the one that exists.
- **What this spec owes instead is that the existing test keeps working.**
  `e2e/overlays.spec.ts` must pass **unmodified** — it is not in the engineer's file
  list, and a diff touching it is a review blocker (T012). That is the characterisation,
  and it is a real one: if `unavailable` emitted `disabled` instead of `aria-disabled`,
  the test's `toHaveAttribute('aria-disabled', 'true')` at line 447 fails; if it
  dropped the attribute when false, line 356 fails; if the conversion disturbed the
  form guard, `patchRequestCount` goes 1 → 2 at line 451.

**The rule this spec writes down for the next author** (FR-006, in the doc comment):
a **new** submit-button call site of `unavailable` owes a Playwright test pressing
Enter **in a text field**, proven by disabling the guard and watching the request count
rise. A vitest/`user-event` test of that path measures a synthetic click and proves
nothing (ADR-0151, spec 160).

**Honest residual:** `e2e/layouts.spec.ts` has no implicit-submission test, and
`LayoutEditorDialog` has the same submit button. That gap exists on `develop` today,
is not created or widened by this slice, and closing it means a second full-stack e2e
scenario. Recorded in §7 as a follow-up, not smuggled into this slice.

### Q3 — Should any existing call site convert?

**Yes: the two Save buttons. No: the four bare-`<button>` sites.**

ADR-0151 §4 binds "any control a spec is already touching". This spec touches `Button`,
so every `Button` call site with the hand-rolled pattern is in its blast radius — and
that is exactly two. Converting them is what gives the prop a production consumer, which
issue #2399 names as the reason not to ship a propless-consumer prop.

The four bare-`<button>` sites are *not* "controls this spec is touching": they do not
import `Button` and this spec does not change them. Adopting the prop there means first
adopting `Button` there, which is a visual change to Undo/Redo/Retry/Reload with no
behavioural benefit (§3). Recorded as a follow-up in §7, not as debt this slice is
hiding: those four sites are already correct, already guarded, and already tested.

### Q4 — What does "done" look like, given nothing may be swept?

Not "a prop exists". Six observable criteria, each with the counterfactual that proves
its test discriminates. Every counterfactual is constructed in the **subject's** file
(`Button.tsx`) while the expectation lives in a **different** file — the criterion spec
162 extracted.

| # | Criterion | Counterfactual in `Button.tsx` | Expectation lives in |
|---|---|---|---|
| D1 | `unavailable` renders `aria-disabled="true"` and **not** the native `disabled` attribute | emit `disabled={unavailable}` instead | `Button.test.tsx` |
| D2 | An `unavailable` Button is still **focusable** and still **receives click events** | as D1 — native `disabled` blurs and swallows clicks | `Button.test.tsx` |
| D3 | `unavailable={false}` renders `aria-disabled="false"`, not nothing | emit `unavailable \|\| undefined` | `Button.test.tsx` **and** the 8 existing dialog assertions (§1.5) |
| D4 | An `unavailable` Button carries `aria-disabled:opacity-50` | drop the class from the emitted list | `OverlayEditorDialogSaveGate.test.tsx`, `LayoutEditorDialogSaveGate.test.tsx` |
| D5 | `unavailable` is **not** forwarded to the DOM as a stray attribute | stop destructuring it out of `...rest` | `Button.test.tsx` |
| D6 | Every existing suite and `e2e/overlays.spec.ts` passes **unmodified** | any of D1–D4 | the suites themselves; the diff is the evidence |

"Done" is: D1–D5 have tests that were **observed red** (D1, D2, D5) or **observed
green on `develop` first and green again after** (D3, D4), the counterfactual output
for at least D1/D2 and D4 is quoted in the PR body, and the diff touches only the files
in §6.

---

## 5. Functional requirements

- **FR-001** — `Button` accepts an optional `unavailable?: boolean`. When it is
  provided, the rendered element carries `aria-disabled` with that value and the
  Tailwind class `aria-disabled:opacity-50`. When it is not provided, neither appears
  and the rendered output is byte-identical to today's.
- **FR-002** — `unavailable={false}` renders `aria-disabled="false"`. Absence of the
  attribute is **not** an acceptable rendering of `false` (§1.5).
- **FR-003** — The prop emits dimming (`opacity-50`) only. Cursor treatment
  (`aria-disabled:cursor-progress`) stays a call-site `className`, because it claims
  *busy*, not *unavailable* (§1.6).
- **FR-004** — `unavailable` is consumed by `Button` and never reaches the DOM as an
  attribute.
- **FR-005** — `Button` does **not** emit `pointer-events-none` for `unavailable`. The
  control must stay clickable; that is the entire point of ADR-0151 §1, and the
  handler guard is what refuses.
- **FR-006** — The prop carries a doc comment stating, at minimum: when to reach for
  `unavailable` rather than `disabled` (ADR-0151 §1 — a control that can become
  unavailable **while it holds focus**, canonically because activating it is what makes
  it unavailable); that the caller's guard is mandatory and `aria-disabled` without one
  is a regression (§2); that for a submit button the guard belongs on the **form's**
  `onSubmit`; and that a new submit-button site owes a real-browser Enter-in-a-text-field
  test (§4 Q2). Reference ADR-0151 by number.
- **FR-007** — `OverlayEditorDialog.tsx` and `LayoutEditorDialog.tsx` Save buttons use
  `unavailable={saveBlocked}` and retain `className="aria-disabled:cursor-progress"`.
  `saveBlocked`, `handleFormSubmit` and `saveRef` are **not** modified. The rendered
  attribute set and class set are unchanged (order may differ; assertions are
  order-independent).
- **FR-008** — ADR-0151's population figures are corrected to 26 native / 13 files and
  6 aria / 4 files, with the reproduction command, in both the Context ("The
  population") and §4. No decision text changes.

## 6. Acceptance scenarios (Gherkin)

**Happy path — the prop announces without natively disabling**

```gherkin
Scenario: an unavailable Button is dimmed, announced, and still reachable
  Given a Button rendered with unavailable={true}
  When the rendered element is inspected
  Then it has aria-disabled="true"
   And it does not have the native disabled attribute
   And it has the class "aria-disabled:opacity-50"
```

**The behaviour the ADR exists for — focus survives**

```gherkin
Scenario: an unavailable Button keeps the operator's place
  Given a Button rendered with unavailable={true}
  When the element is focused
  Then document.activeElement is that element
  When the element is clicked
  Then its onClick handler is invoked
```

*(The handler being invoked is the point: `aria-disabled` is an announcement, and the
caller's guard is what refuses. A test asserting the handler is NOT invoked would be
asserting the opposite of ADR-0151 §1.)*

**The `false` case — the one that breaks eight existing assertions if got wrong**

```gherkin
Scenario: an available Button says so explicitly
  Given a Button rendered with unavailable={false}
  Then it has aria-disabled="false"
   And it does not have the native disabled attribute
```

**The untouched case — no regression for the 20-odd other call sites**

```gherkin
Scenario: a Button with no unavailable prop is unchanged
  Given a Button rendered without the unavailable prop
  Then it has no aria-disabled attribute
   And it has no "aria-disabled:opacity-50" class
   And it still has the base "disabled:opacity-50" and "disabled:pointer-events-none" classes
```

**Bad request — the contradiction**

```gherkin
Scenario: unavailable and disabled together
  Given a Button rendered with both unavailable={true} and disabled
  Then it has aria-disabled="true"
   And it also has the native disabled attribute
   And the control is NOT focusable
```

*(Documented, not prevented — §4 Q1. The scenario is specified so the behaviour is
known rather than discovered: `disabled` wins because the browser enforces it. The doc
comment says do not do this.)*

**Conflict / auth:** N/A. `Button` is a presentational primitive with no network call,
no scope check and no server state. There is no 409 and no 401 on this path. The
conflict behaviour of the *dialogs* the prop is adopted into is spec 160's and is
covered by the suites that must pass unmodified (§1.5, §1.4).

**Characterisation — the conversion changes nothing observable**

```gherkin
Scenario: the Save button renders identically before and after adoption
  Given the overlay editor dialog open on an existing draft with a stale chain
  When the Save button is inspected
  Then it has aria-disabled="true"
   And it has the classes "aria-disabled:opacity-50" and "aria-disabled:cursor-progress"
   And it does not have the native disabled attribute
  # Captured GREEN on develop before the conversion; must pass UNMODIFIED after.
```

## 7. Independent end-to-end test procedure

A human, or the phase-5 agent, can verify this without reading the diff:

1. `cd apps/shared && npx vitest run src/ui/primitives/Button.test.tsx` — green, and
   `npx vitest list --run | grep Button.test` prints the file (memory: *tests that
   actually run* — a test file the config does not collect is not coverage).
2. `cd apps/management-web && npx vitest run src/features/overlays src/features/layouts`
   — green, with **no diff** to any of those test files beyond the two new
   characterisation assertions (T004).
3. Boot the stack, open the overlay editor on an existing draft in two browser
   contexts, save from the first, then in the second: click Save, and **while the PATCH
   is in flight** press `Tab`. Focus must move to the *next* control after Save, not to
   the top of the document — i.e. Save still held focus. This is ADR-0151 §1's actual
   claim, observed rather than asserted.
4. In the same in-flight window, click into the overlay text field and press `Enter`.
   No second PATCH is issued (watch the network panel). This is the trap, observed by
   hand.
5. `git diff origin/develop --stat` shows no file under `apps/management-web/src/features/`
   other than the two dialogs and their two test files, and `e2e/` untouched.

## 8. Locked tech choices

React + TypeScript + Vite (ADR-0074) · Radix Slot for `asChild` and Tailwind tokens via
CSS custom properties (ADR-0077, ADR-0078) · `clsx` for class composition (already in
`Button.tsx`) · vitest + `@testing-library/react` + jsdom environment comment, mirroring
`ConfirmDialog.test.tsx` (ADR-0052) · sentence-style test names with underscores or
prose `it('…')` as the neighbouring file does (ADR-0053) · Playwright for e2e
(ADR-0108) — **no new Playwright test in this slice** (§4 Q2).

No new dependency. No new pattern: `unavailable` is the same conditional-class idiom
`Button` already uses for `variant`.

## 9. Follow-ups (not this slice)

- **`[DECISION REQUIRED]`** — the runtime coupling of §4 Q1. Needs an ADR extending
  ADR-0151 if wanted, and must answer reason 2 (it neutralises spec 160's
  counterfactual).
- `e2e/layouts.spec.ts` has no implicit-submission test (§4 Q2, honest residual).
- The four bare-`<button>` `aria-disabled` sites could adopt `Button` + `unavailable`,
  at the cost of a visual change to four controls (§4 Q3).
- The 26 native sites remain, deliberately (ADR-0151 §4). ADR-0151's Alternatives
  records the sweep as "the honest upgrade path if the two spellings prove confusing in
  practice".
