# Spec 280 — The region that forgets clearing

**Issue**: #2365 · **Branch**: `fix-2365-always-mount-error-alerts` · **Phase**: 1 (Specify)
**Date**: 2026-09-28 · **Base**: `c709495b` (`origin/develop`)
**Context**: `apps/shared/src/ui/composites/OverlayGeometryFields.tsx` and its test file only.
**Engineer**: delivered directly in this session · **Reviewer**: self-review against the diff (§6)
**Phase 4a colour**: **behaviour-preserving refactor for the four field error regions' accessibility
shape** (always-mounted vs on-demand is an implementation detail; the operator-visible content — which
message, when — does not change) **plus one genuinely new fact** (the "present with empty text" shape
itself needs a first observation). Treated as characterisation for the ~8 rescoped tests (captured green
against current `develop` before the change, must pass unmodified... except they structurally cannot,
because the query shape itself has to change from unscoped to scoped — see §5 for how this is handled
honestly) and as new-behaviour (red-first) for the two tests the issue named plus the multiplicity guard.
**ADRs**: none directly; follows the precedent `OverlayEditor.tsx:534` and this same file's own advisory
region (#2346 phase-6 should-fix 4) already established for an always-mounted live region.
**Constitution**: §IV — N/A. Pure frontend accessibility/test-shape change, no leg of the event→overlay
path.
**New ADR needed**: **No.** Applies an existing, already-adopted pattern (always-mounted live region,
content toggles) to four more regions of the same kind.

---

## 1. The defect

Each of the four geometry fields' error message is a `role="alert"` `<span>` mounted only while
`error !== undefined` (`OverlayGeometryFields.tsx:288-292`). Two tests prove "the message cleared" by
asserting the node is **absent** (`queryByRole('alert')).toBeNull()`), which is the same trap #2346's
phase-6 review already found and fixed for the FR-012 advisory region next to these: a live region
inserted into the DOM at the same instant as its content is not reliably announced by screen readers, and
an absence-based test cannot distinguish "correctly cleared" from "never reliably announced."

## 2. Product-owner decision (made; not re-opened here)

Always-mount all four error regions, matching the advisory region's existing fix — not merely narrow the
test shape while keeping on-demand mounting. Surfaced and confirmed with the real cost priced in: because
all four fields render from the same `FIELD_SPECS.map(...)`, always-mounting means **up to four
`role="alert"` elements exist simultaneously**, which breaks every existing test in the file that queries
`getByRole('alert')` / `queryByRole('alert')` / `queryAllByRole('alert')` unscoped (an assumption that
"at most one alert exists" was implicit and untested on its own). All such queries are rescoped to the
specific field under test as part of this change — not left for a later PR.

## 3. Scope

**In**:
- `OverlayGeometryFields.tsx`: remove the `error !== undefined && (...)` conditional; the error `<span>`
  is always rendered, `{error ?? ''}` as its content — mirrors the advisory region exactly.
- A `data-testid={`overlay-geometry-error-${spec.field}`}` on each error span, so a test can address one
  field's region directly without relying on element count.
- `OverlayGeometryFields.test.tsx`: every existing `getByRole('alert')` / `queryByRole('alert')` /
  `queryAllByRole('alert')` assertion rescoped to the field it actually concerns, via the new testid.
  Content changes only where the assertion's own subject requires it (the two "cleared" tests: from
  "absent" to "present, empty").

**Out** — do not do:
- Touch `aria-describedby`'s own conditional (`error !== undefined ? errorId : undefined` on the input) —
  that wiring is unaffected by whether the *span* is mounted, and the issue does not ask for it.
- Touch the FR-012 advisory region (already fixed, #2346) or `ReservedMessageSlot`'s layout mechanism
  (#2366) — both are correct as-is and this change reuses them unmodified.
- The one `queryByRole('alert')).toBeNull()` at test line ~452 (off-edge-but-accepted case) — a genuine
  "never shown at all" scenario, not a "was shown, now cleared" one; the issue explicitly says this one is
  correct as written.
- `committedValueError` / `validate` / any validation logic — only the error's *presentation* changes.

## 4. User story

### US1 (P1) — A cleared field error is provably cleared, not merely unobserved

As a screen-reader user editing overlay geometry, I want a field's error region to already exist in the
DOM before and after a refusal — content changing, not the node appearing/disappearing — so the region is
reliably announced both when a refusal appears and (implicitly, by matching the advisory region's already-
verified pattern) when it clears; and as a test author I want "cleared" expressed as "present with empty
text," not "absent," so a future implementation change that fixes this shape does not fail these two
tests for getting the accessibility better, the same trap the advisory test escaped in #2346.

**Independent test**: the two originally-named facts assert `getByTestId(...).textContent` is `''` after
clearing rather than `queryByRole('alert')` being `null`; a new fact asserts all four regions are mounted
(and empty) before any refusal, mirroring the advisory's own always-mounted guarantee.

## 5. Test-shape honesty (why this isn't a clean red/green split)

This change's *production* behaviour for an operator is unchanged — the same message appears and clears
at the same moments. What changes is the DOM shape the tests observe. That makes most of the ~8 rescoped
assertions neither a clean "red before, green after" (the old query shape doesn't compile-fail or
misbehave against old code — `getByRole('alert')` still works today for a single-field test, since only
one field has an error at a time in each of those tests) nor a strict behaviour-preserving refactor (the
DOM shape being asserted against genuinely changes from "0 or 1 alert nodes" to "always 4").

Handled as: every rescoped assertion is captured passing against **current `develop`** first (old query
shape, old component) — this is the "before" a reader can compare against in the PR diff. Then the
component changes and every assertion's *query* changes to the scoped `data-testid` form, verified passing
against the new shape. The two tests the issue named, plus the new multiplicity-guard fact, are the
closest thing to genuine new-behaviour red/green here — captured as such in the PR body. Every other
rescoped assertion's own **claim** (which message, when) is unchanged; only how the test locates the node
changes, which is disclosed rather than presented as an unbroken red-then-green chain it structurally
cannot be.

## 6. Design

Exactly the advisory region's shape, four times: `{error ?? ''}` instead of `{error !== undefined && (...)}`,
one `data-testid` per field keyed on `spec.field` (already the map key, so no collision risk).

**Why per-field testid rather than `within(messageSlotTestId)`:** `ReservedMessageSlot`'s own
`data-testid={`overlay-geometry-message-slot-${spec.field}`}` wraps *both* the hidden reserved-space
candidates and the live span — `within(...).getByRole('alert')` would work too, but a direct testid on the
live span itself is one call instead of two and matches the advisory region's own
`data-testid="overlay-geometry-advisory"` convention exactly (that one has no `within` step either).
