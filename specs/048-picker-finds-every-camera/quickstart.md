# Quickstart — 048 the camera picker finds every camera

How to see the defect, and how to see it fixed. Written so someone who did not
write the feature can reproduce both.

---

## Seeing the defect (before)

The picker requests one page of 50 cameras ordered by registration date,
descending, and renders exactly those.

**The catch that makes this awkward to demonstrate**: a fresh dev stack has
nowhere near 50 cameras, so the picker looks perfect. The defect only appears
once a fab passes 50 — which is how it survived until a stack that had
accumulated 51 hit it during spec 046's verification walk.

**To reproduce deliberately**, you need more than 50 cameras in one fab. Either:

- register 51+ through the management UI or the API, or
- use a stack that has accumulated them (the e2e seeds add two per run, which is
  what produced the original 51 — tracked as its own issue).

Then open **Layouts → New layout** and look at a tile's Camera dropdown. The
51st-oldest camera is not there. Nothing says so. An operator concludes it was
never registered.

---

## Seeing it fixed (after)

Same steps, and three things are different:

1. **The dropdown is alphabetical**, not newest-first. This is the visible
   change and the one an operator will notice immediately.
2. **Every camera is there**, up to 1000.
3. **If the fab has more than 1000**, the dialog says so — how many are shown
   and how many exist — and the notice is announced when a camera dropdown takes
   focus, not merely painted on screen.

With a fab under 1000 there is **no notice at all**. That absence is the point:
a notice that is always present carries no information.

---

## Checking it properly

### The arithmetic, without a browser

The paging lives in `apps/shared` and is testable directly:

```sh
cd apps/shared && npx vitest run src/api/cameras.api.test.ts
```

Covers page count, de-duplication at a boundary, the bound, and the count
passthrough. **This is where a boundary bug is caught** — a component test that
renders 250 options proves the total is right without proving which camera was
dropped at offset 200.

### The dialog

```sh
cd apps/management-web && npx vitest run src/features/layouts/
```

Both directions of the notice: present when truncated, **absent when complete**.

### The whole thing, as CI runs it

Not a subset. Spec 045 shipped a green subset and CI caught an architecture test
never run locally.

```sh
pnpm format:check
pnpm -r --filter "./apps/**" lint
pnpm -r --filter "./apps/**" typecheck
pnpm -r --filter "./apps/**" test
```

---

## What none of that establishes

Every check above runs against a fixture. **None runs against a real fab of 250
cameras**, so a green suite proves the paging arithmetic is right and the notice
appears when it should. It does not prove:

- that two round trips feel acceptable to an operator opening the dialog;
- that a 250-option dropdown is *usable*, as opposed to *correct*;
- that 1000 is the right bound — nothing was measured, it is four times the only
  scale number the constitution states.

The first two need a person and a populated fab. The verification note must
record which of those actually happened — and if the populated fab was not
available, say that, rather than quietly narrowing the claim to what the
fixtures covered.

This warning is here because spec 046 shipped a defect that **all sixteen of its
mutations missed**: the tests covered the transitions they had thought of, and
could not cover a starting state they never constructed. The blind spot here is
scale.
