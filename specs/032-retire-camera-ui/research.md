# Phase 0 Research: Retire a camera from the management app

**Feature**: `032-retire-camera-ui` · 2026-08-24

Five questions the plan refused to assume its way past. Two of the answers
changed the shape of the work; one is a finding to raise rather than absorb.

---

## 1. Is there a destructive-confirmation precedent to follow?

**No. There is none, anywhere in the product.**

```sh
grep -rniE "confirm|alertdialog|are you sure" apps/*/src --include=*.tsx --include=*.ts
# (no matches outside tests)
```

Four pages already perform a terminal-ish action — `RulesPage`, `OverlaysPage`,
`LayoutsPage`, `SystemVariablesPage` all archive — and every one of them does it
on a **single click, with no confirmation of any kind**.

**Decision**: this feature introduces the product's first destructive
confirmation, and the plan says so plainly rather than pretending to follow a
pattern. The shape it establishes is what the next one will copy, so it is built
as a **shared primitive**, not as a camera-specific dialog.

**Alternatives considered**: imitating one of the four archive flows — rejected,
because they are the thing this feature exists to *not* do. Building it
camera-locally — rejected, because the second destructive action would then
either duplicate it or diverge from it, which is how the stale-version
divergence in spec 031 started.

### Finding to raise, not absorb

**Archiving a rule, overlay, layout or system variable takes one click and asks
nothing.** Whether those should confirm is out of this spec's scope — it is four
other features' behaviour — but it is now a visible inconsistency: cameras will
confirm and everything else will not. **File an issue**; do not quietly change
the other four here, and do not quietly skip the confirmation for cameras to
match them.

---

## 2. Is `AlertDialog` available, or only `Dialog`?

**Only `Dialog`.** `apps/shared/package.json` carries seven Radix packages —
`react-dialog`, `react-dropdown-menu`, `react-popover`, `react-select`,
`react-slot`, `react-tabs`, `react-tooltip` — and **not**
`@radix-ui/react-alert-dialog`.

The existing `apps/shared/src/ui/primitives/Dialog.tsx` wraps
`RadixDialog.Root/Overlay/Content/Title/Description`.

**Decision**: add `@radix-ui/react-alert-dialog` at the `1.1.x` line the other
primitives use, and build a shared `ConfirmDialog` primitive on it.

**Rationale**: the difference is not decorative. An alert dialog carries
`role="alertdialog"`, which is announced more assertively, and it defaults focus
to the cancelling action rather than the first focusable element — which for an
irreversible action is the difference between a stray Enter cancelling and a
stray Enter retiring a camera. Adding another Radix primitive **is** ADR-0077
("Radix UI headless components + custom design system"), not a deviation from
it.

**Alternatives considered**: passing `role="alertdialog"` through to the
existing `Dialog`'s `Content`. Radix spreads unknown props, so it would render —
but it produces a component whose ARIA role and whose focus behaviour disagree,
and hand-rolling the parts Radix would have supplied is exactly the subtle-bug
surface a headless library exists to remove. Rejected.

---

## 3. There is no `danger` button variant

`Button` exposes `primary | secondary | ghost`. A confirm-and-retire control
rendered as `primary` looks identical to *Save* and *Register*.

**Decision**: add a `danger` variant to the shared `Button`, using existing
design tokens (ADR-0078). Small, and it belongs with the confirmation primitive
rather than being invented at the call site with a `className` override.

---

## 4. What must the mutation invalidate?

The existing shape:

| Endpoint | Tags |
|---|---|
| `getCamera` | provides `{ Camera, id: <identifier> }` |
| `listCameras` | provides one tag per row **plus** `{ Camera, id: 'LIST' }` |
| `changeCameraAddress` | invalidates `{ Camera, id }` **and** `{ Camera, id: 'LIST' }` |

**Decision**: `retireCamera` invalidates **exactly the same two**.

**Rationale**, and the reason this was worth checking rather than assuming: RTK
Query invalidation **refetches** for components still subscribed, rather than
dropping the entry. So one invalidation satisfies two requirements that sound
opposed:

- `{ Camera, id }` — the detail page is mounted and subscribed, so it refetches
  and re-renders with `status: Decommissioned`. That is **FR-009** (new state,
  no full reload) and **FR-011** (the record still reads) at once. Nothing needs
  to be hand-written to keep the retired camera readable; it stays readable
  because the endpoint still serves it.
- `{ Camera, id: 'LIST' }` — the listing refetches and the camera is absent,
  because the API excludes retired cameras by default. That is **FR-010**.

**Alternatives considered**: optimistic local update of the cached camera —
rejected. It would show `Decommissioned` before the server agreed, and the one
case where that lies is precisely the case FR-012 is about.

---

## 5. Can Playwright drive this honestly?

**Yes, and spec 030 left a note saying exactly when it would become possible.**

`e2e/camera-detail.spec.ts` carries a comment where the retired-camera test
would have been:

> *"Nothing in the app can retire a camera (#1860), so an end-to-end test would
> have to reach around the UI and call the API to arrange its own state. The
> first attempt did exactly that and failed for two reasons at once: a relative
> fetch resolves against the app's origin rather than the gateway's, and it
> carried no bearer token. … This becomes writable honestly once #1860 lands."*

That is this feature. The seam is already there:

- `signInAsOperator(page)` handles OIDC.
- A local `registerCamera(page)` helper arranges state **entirely through the
  app** — clicking *Register camera*, filling the form, submitting.

**Decision**: SC-005's test registers, retires, checks the listing and re-opens
the address, driving the app throughout. **No `fetch` to the API appears in this
spec's e2e.** If arranging state ever seems to need one, that is the signal
spec 030 acted on — stop and reconsider, rather than adding a token.

---

## 6. What does the app *say* on success?

There is **no toast or notification infrastructure** in either app.

That turns out to resolve **FR-012** rather than complicate it. FR-012 forbids
claiming *this operator* retired the camera, because the endpoint answers `204`
whether or not they did.

**Decision**: the page's own state is the feedback. The camera is shown as
retired, the retire control is gone, and **nothing announces authorship** —
because nothing announces anything. The confirmation's *forward-looking* wording
("this will…") is a description of the operation, not a claim about who
performed it, so it stays.

**Rationale**: with no toast infrastructure, the tempting implementation is to
add one and write *"Camera retired."* — a sentence the app cannot support in the
already-retired case. Not building it is both smaller and more truthful.

**Alternatives considered**: an inline success banner on the page. Same problem
in a smaller box, plus it competes with the retired-state notice already there.
Rejected.

---

## Summary of decisions

| # | Question | Decision |
|---|---|---|
| 1 | Confirmation precedent | None exists. Build the **first**, as a shared primitive. Raise the inconsistency as an issue |
| 2 | AlertDialog | Add `@radix-ui/react-alert-dialog`; correct role and correct default focus |
| 3 | Danger styling | Add a `danger` variant to the shared `Button` |
| 4 | Cache tags | `{ Camera, id }` + `{ Camera, 'LIST' }` — one invalidation serves FR-009, FR-010 and FR-011 |
| 5 | e2e | Drive the app end to end; **no API reach-around**, per spec 030's recorded lesson |
| 6 | Success wording | Say nothing. The page state is the feedback (FR-012) |

**No backend change is required.** The endpoint's declared contract —
`204/400/403/404`, no `409/412/428` — matches FR-016 exactly.
