# Spec 156 — A recovery control that survives its own activation

**Issue:** #2372
**Branch:** `fix/2372-a-recovery-control-that-survives`
**Phase:** 1 (Specify) — ADR-0037
**Lane:** autonomous (ADR-0144); issue carries `agent:ready`
**Scope:** `apps/management-web`, `apps/shared` (UI only). Nothing under `src/**`.
**ADRs:** ADR-0077 (Radix + custom design system; "zero accessibility debt"),
ADR-0074 (two frontend apps), ADR-0078 (Tailwind design tokens),
ADR-0113 / ADR-0119 (why a chain re-read blocks Save, and the vocabulary for a
refused version), ADR-0037 (this workflow), ADR-0144 (the lane).

**Latency budget (constitution §IV): N/A.** This is `apps/management-web` — the
operator console. The event→overlay path runs through `apps/kiosk-web` and
`apps/shared`'s viewer; nothing here is on any of the six legs. The one file
touched in `apps/shared` is a UI composite consumed only by the management
dialogs. No leg is affected and none is cited.

---

## What was found, against what the issue says

The issue is right that the defect exists, right about which files, and right
that it is a house pattern rather than a regression. Three things it says are
wrong or incomplete, and each one changes the fix.

### 1. The control is destroyed at **activation**, not on success

The issue says: *"When the refetch succeeds the condition flips, the `<p>`
unmounts, and it takes the focused button with it."*

It unmounts the moment Retry is **clicked**. `chainFailed` is RTK Query's
`isError`, which is `status === 'rejected'`
(`@reduxjs/toolkit/dist/query/rtk-query.modern.mjs:19`). `refetch()` writes
`substate.status = STATUS_PENDING` unconditionally for every pending entry
(`:1287`), so `isError` goes **false while the request is in flight** and only
returns to true if it is refused again.

So between the click and the response the dialog renders:

- no `role="alert"` (the ternary has flipped to the `backendError` arm, and
  `backendError` is `null` — nothing has been submitted),
- no Retry button,
- Save still disabled (`currentChain === undefined`),
- `document.activeElement` = the dialog `<div>`.

The measurement at 20 ms is consistent with both readings; the mechanism is
not. **A fix that moves focus "on success" leaves the entire in-flight window
unfixed** — and that window is the one the operator actually experiences as a
dead dialog, because it is the one that lasts a network round trip.

### 2. There are **four** controls, not three — `OverlayEditorDialog` has a Reload too

The issue quotes Overlay's Retry and says *"`LayoutEditorDialog.tsx` does the
same for both **Retry** (`:357`) and **Reload** (`:369`)"*, which reads as
Overlay having one control. It has two: Retry at `:278-283` and Reload at
`:286-294`, gated on `offerReload = staleConflict || notDraft`
(`OverlayEditorDialog.tsx:228`). The two dialogs are structurally identical —
one ternary, two alerts, one control in each arm.

### 3. Reload does **not** exhibit the focus defect, and must not be "fixed" as if it did

`backendError` is derived from the **mutation's** `error`
(`OverlayEditorDialog.tsx:215`, `LayoutEditorDialog.tsx:237`). `refetchChain()`
touches the **query**. Nothing in either dialog resets the mutation except the
close effect. So clicking Reload leaves its alert and its button mounted and
focused.

What Reload *does* share is the silence: nothing announces that a re-read is
under way, and in `LayoutEditorDialog` Save goes disabled for the duration
(`:416`, `chainFetching`). So Reload needs the announcement and must **not** get
a focus move — moving focus away from a control that is still there and still
correct would be a new defect. This is recorded as FR-008 because the obvious
reading of the issue is to treat the two identically.

### 4. The census is 16 sites, not 2

A systematic sweep of `apps/` for a focusable control inside a live region found
**16**, of which 14 are outside this spec's scope:

| Where | Sites |
|---|---|
| `OverlayEditorDialog.tsx` `:278`, `:286` | 2 — **in scope** |
| `LayoutEditorDialog.tsx` `:361`, `:369` | 2 — **in scope** |
| `SystemVariablesPage.tsx` `:99`, `:111` | 2 |
| `LayoutsPage.tsx` `:113`, `:125` | 2 |
| `OverlaysPage.tsx` `:111`, `:123` | 2 |
| `RulesPage.tsx` `:150`, `:169` | 2 |
| `CamerasPage.tsx` `:124` | 1 |
| `AuditPage.tsx` `:150` | 1 |
| `ShellLayout.tsx` `:103` (`CrashPanel`, "Try again") | 1 |
| `kiosk-web/.../ReconnectingScreen.tsx` `:14` ("Try now") | 1 |

Eight of those are the *same* self-destroying shape (activating the only control
inside the alert is the only way to dismiss it). Six add a second hazard: the
button is nested in its own condition inside an already-mounted region, so it
can appear without the region re-announcing.

**This spec does not fix the other 14.** They are page-level list refetches with
different state (no chain version, no Save to hand focus to), and a 16-site
change is not the smallest independently-shippable slice. A follow-up issue
carrying this census is a deliverable of phase 3 (T009). What the spec does do is
make the two dialogs the worked reference the other 14 can be brought to.

The census also shows there is **no shared live-region helper anywhere** — every
one of the five `aria-live` regions in the repo is hand-rolled at its usage site,
and the only two solutions to the repeated-announcement problem (#2344) are both
private to `OverlayEditor.tsx`.

---

## The locked answers

### Where focus goes on success — **Save**

Rejected alternatives, and why:

- **Keep the control mounted permanently.** A "Retry" button with nothing to
  retry is noise on every edit, and it would be either natively `disabled` (which
  blurs — spec 154's finding, `OverlayEditor.tsx:649-657`) or a live control that
  does nothing.
- **Focus the status region** (`tabIndex={-1}`). Works, but parks the keyboard
  operator on a non-interactive node whose only value is being read aloud once.
- **Leave focus where it falls and announce only.** Announcement alone does not
  restore the operator's position in the form; the issue names focus loss as one
  of three problems, not a symptom of the other two.

**Save wins because it is what the operator was trying to reach.** The chain read
is the only thing blocking Save (FR-002 of spec 153); an operator clicks Retry
*in order to* Save. The re-read succeeding is exactly the moment Save becomes
enabled, so focus lands on a control that is newly actionable rather than on a
consolation prize.

### The in-flight window — the control stays, `aria-disabled`

Because of finding 1, focus must be **kept**, not restored. The recovery control
stays mounted while `chainFetching` is true and is marked `aria-disabled`, never
natively `disabled` — a browser blurs a focused element the instant it becomes
natively disabled, which would reproduce the very defect being fixed. This is the
precedent `OverlayEditor.tsx:649-657` set for Undo/Redo and its reasoning carries
over verbatim.

### What announces it — an always-mounted `role="status"`

Per #2346: a live region inserted into the DOM at the same instant as its content
is not reliably announced. The region is mounted for the life of the dialog in
edit mode and its **text changes**. This mirrors
`OverlayGeometryFields.tsx:242` (`advisory ?? ''`) and
`OverlayEditor.tsx:639`, the two places the repo already got this right.

Per #2344: writing an unchanged string is a React no-op, so a second identical
message is silent. Two retries in a row both say "Re-reading…", so the region
needs the established de-duplication — either the toggled zero-width space
(`OverlayEditor.tsx:387-398`) or the `key`-token remount
(`OverlayEditor.tsx:501-508`). The plan picks one.

### `role="alert"` vs `role="status"` — both, for different things

`alert` is assertive and is correct for **the two error banners**: a chain read
that failed blocks Save outright, and a refused submit is a refusal. Spec 151
drew this line — *"neither the schema nor the domain calls this a problem, and an
`alert` interrupts a screen reader for something that is not one"*
(`specs/151-.../plan.md:191-194`) — and a transient "re-reading…" is on the far
side of it. So: **errors stay `alert`, progress and recovery are `status`.**

The error banners also stay **mounted on demand**. Assertive regions *are*
announced on insertion, and because of finding 1 the failure path genuinely
remounts (pending → rejected), so today's failure announcement is correct and is
not touched.

### Relationship to #2365 — **stay clear, and say why**

#2365 asks whether the geometry panel's two FR-007 error regions
(`OverlayGeometryFields.tsx:228`) should become always-mounted. Different file,
disjoint from everything here.

This spec **neither subsumes nor blocks it**, and deliberately does not answer it
by side effect. It does hand #2365 the argument it was missing: this spec keeps
`role="alert"` mounted on demand *on purpose*, on the grounds that assertive
insertion announces reliably and the failure path in these dialogs demonstrably
does. If #2365 accepts that, its answer is "keep them, and fix the test shape";
if it rejects it, it should say why the geometry panel differs. Either way the
two changes touch no common line.

---

## User stories

### US1 (P1) — An operator retrying a failed overlay read keeps their place

**As** an operator editing an overlay draft whose chain read failed,
**I want** the Retry control to still be there while it retries, and my focus to
land on Save when it works,
**so that** I can finish the edit without re-traversing the form or guessing
whether my click did anything.

**Independently shippable:** yes. `OverlayEditorDialog` alone is a complete,
observable slice.

### US2 (P1) — The same, for a layout draft

**As** an operator editing a layout draft,
**I want** identical behaviour,
**so that** the two editors do not teach me two different things.

**Why also P1:** the issue exists *because* these two drifted into the same
defect independently. Shipping one without the other re-creates the condition.

---

## Functional requirements

- **FR-001** — Each dialog renders exactly one `role="status"` region that is
  **mounted for the whole life of the dialog**, in both create and edit mode,
  and whose text changes. It is never conditionally mounted. It is visually
  hidden (`sr-only`) and carries a `data-testid` so a test can address it
  without JSX-order guessing (the reason `OverlayEditor.tsx:631-638` gives).
  **Correction (phase-6 review):** this said "in edit mode" — the shipped
  composite is not gated on `isEdit` in either dialog, and that is not a
  defect: the chain query is `skipToken` outside edit mode, so `readFailed`/
  `reReading` are already inert there, and create mode has its own
  `backendError` (a name clash) that needs the same region regardless. Gating
  on `isEdit` would have silently dropped that create-mode banner. Wording
  corrected to match what was actually built.
- **FR-002** — While a chain re-read is in flight the region reads
  *"Re-reading the overlay…"* / *"Re-reading the layout…"*.
- **FR-003** — The recovery control stays mounted for the whole in-flight
  window. Its mount condition becomes "the read failed **or** a re-read is in
  flight", not "the read failed".
- **FR-004** — While the re-read is in flight the control is `aria-disabled`,
  **not** natively `disabled`, and its click handler refuses to act. Focus is
  never taken from it by the framework.
- **FR-005** — When a re-read started by the operator **succeeds**, the control
  unmounts — there is nothing left to retry — and focus moves to Save, which the
  success has just enabled. The region reads *"The overlay was read. Save is
  available."* (…*layout*…).
- **FR-006** — When a re-read started by the operator **fails again**, focus
  stays on the control, the `role="alert"` returns with its message, and the
  region is cleared. No focus is moved.
- **FR-007** — A chain read that succeeds **without the operator having
  activated a recovery control** moves no focus and announces nothing. This
  covers every normal dialog open; stealing focus to Save on first load would be
  a worse defect than the one being fixed.
- **FR-008** — The **Reload** control (the `backendError` arm) gets FR-002's
  announcement and gets **no focus move**. Its alert is mutation-derived and
  `refetchChain()` does not clear it, so the control is still mounted and still
  focused when the re-read finishes; moving focus away from it would be new
  damage. The reason is recorded at the call site.
- **FR-009** — A second consecutive identical announcement is announced again.
  An unchanged string is a React no-op (#2344).
- **FR-010** — Save's `disabled` predicate is **not changed** by this spec in
  either dialog. See §Found and not folded.
- **FR-011** — The two error banners keep `role="alert"` and keep being mounted
  on demand. Their copy is unchanged.
- **FR-012** — Both dialogs exhibit FR-001–FR-011 identically, modulo the noun
  ("overlay" / "layout").

## Non-functional

- **NFR-001** — No change to any request, payload, version, or `If-Match`
  header. This spec is perceivability and focus only.
- **NFR-002** — The behaviour is expressed as tests against the **dialogs**, not
  against any component extracted from them, so the extraction decision in
  `plan.md` can be revised in phase 4 without rewriting a test.

---

## Acceptance scenarios (Gherkin)

### Happy — retry recovers, focus lands on Save (US1, FR-002/003/004/005)

```gherkin
Given the overlay edit dialog is open and the chain read has failed
  And a "Retry" control is shown inside a role="alert"
When the operator activates Retry
Then the Retry control is still in the document
  And it is the active element
  And it is aria-disabled
  And the status region reads "Re-reading the overlay…"
  And no role="alert" claims the read failed
When the re-read returns the current chain
Then the Retry control is gone
  And the Save control is the active element
  And the Save control is enabled
  And the status region reads "The overlay was read. Save is available."
```

### Conflict — the retry is refused again (US1, FR-006)

```gherkin
Given the operator has activated Retry and the re-read is in flight
When the re-read fails
Then a role="alert" reads "The overlay could not be read."
  And the Retry control is in the document, not aria-disabled
  And the Retry control is the active element
  And the Save control is disabled
  And the status region is empty
```

### Repeat — two refusals in a row are both announced (FR-009)

```gherkin
Given the operator has activated Retry once and it failed
When the operator activates Retry a second time
Then the status region's text content differs from the string it held
     during the first re-read
```

*(Differs, not "reads something new to a human": the ZWSP toggle is invisible
and that is the point — the assertion is on the DOM mutation, because the DOM
mutation is what the screen reader reacts to.)*

### Bad request — a refused submit offers Reload, which keeps its own focus (FR-008)

```gherkin
Given the overlay edit dialog is open
  And a submit was refused with a stale-version conflict
  And a "Reload" control is shown inside the role="alert"
When the operator activates Reload
Then the Reload control is still in the document
  And it is the active element
  And the status region reads "Re-reading the overlay…"
When the re-read returns the current chain
Then the Reload control is still the active element
  And focus has not moved to Save
```

### Auth

**N/A, and deliberately so.** Both dialogs render only behind the management
app's authenticated shell, and neither this spec's focus handling nor its
announcements read, send, or gate on a token, a scope, or a fab. No `sse.*`
scope is consulted and none is added. The chain read's own authorization is
unchanged (NFR-001).

### No unasked-for focus move (FR-007)

```gherkin
Given the overlay edit dialog is opening for a draft
When the chain read succeeds and no recovery control was ever activated
Then focus is wherever the dialog put it
  And the Save control is not the active element
  And the status region is empty
```

### Layout parity (US2, FR-012)

```gherkin
Given the layout edit dialog is open and the chain read has failed
When the operator activates Retry and the re-read succeeds
Then the layout dialog behaves exactly as the overlay dialog does,
     with "layout" in place of "overlay"
```

---

## Independent end-to-end test procedure

Not a Playwright run — the whole behaviour is focus and live-region text, both of
which jsdom implements, and the discriminating states (a chain read refused, then
recovering) are far cheaper to drive through the mocked query than through a
real backend. What follows is the **manual** confirmation a human can run once,
which the unit tests stand in for thereafter.

1. Boot the Aspire stack and sign in to `management-web`.
2. Open Overlays, branch a draft, and open its edit dialog. Confirm the dialog
   announces nothing and does not focus Save (FR-007).
3. Stop the overlays API resource from the Aspire dashboard (or block its route)
   so the chain read is refused. Reopen the edit dialog.
4. With a screen reader running, Tab to the Retry control and press Enter.
   - **Expected:** the reader says "Re-reading the overlay"; Tab still moves from
     Retry, not from the top of the dialog.
   - **Today:** silence, and Tab restarts from the dialog container.
5. Press Enter again while still refused. **Expected:** the failure is announced
   both times.
6. Restart the resource, activate Retry once more.
   - **Expected:** "The overlay was read. Save is available.", and the next
     keystroke acts on Save.
7. Repeat 2–6 on a layout draft. The two must be indistinguishable.

---

## Found and not folded

Recorded here so they are not lost and not silently absorbed. Each becomes an
issue in phase 3 (T009).

1. **`OverlayEditorDialog` does not disable Save while the chain is being
   re-read.** `LayoutEditorDialog.tsx:416` includes `chainFetching` in Save's
   `disabled` predicate; `OverlayEditorDialog.tsx:302` does not
   (`isLoading || (isEdit && currentChain === undefined)`). During a **Reload**,
   RTK Query keeps `currentData` for the same argument, so `currentChain` stays
   defined and Save stays live — the operator can submit the pre-Reload version
   while the re-read that would have corrected it is still in flight. That is a
   version-correctness defect (ADR-0113), not a focus one. **FR-010 forbids
   changing it here**: a bug fix changes the bug and nothing else, and folding it
   in would put a concurrency change under an accessibility PR's review.
2. **The other 14 census sites** (§The census is 16 sites). One issue, carrying
   the table, pointing at this spec as the worked reference.
3. **There is no shared live-region helper**, and the repeated-announcement
   problem is solved twice, privately, in one file. Worth an extraction once
   there are more than the two call sites this spec creates — explicitly *not*
   now (no speculative generality).

## Out of scope

- **#2370** — the `data` / `currentData` sweep. Touches the same hooks; not this.
- **#2376** — the CI budget.
- **#2365** — see §Relationship to #2365.
- Anything under `src/**`.
