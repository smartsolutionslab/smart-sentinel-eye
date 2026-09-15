# Plan 163 — a Button that keeps focus

**Phase:** 2 (Plan) — ADR-0037 · **Spec:** [`spec.md`](./spec.md) · **Issue:** #2399
**Engineer:** `frontend-engineer` · **Reviewer:** `frontend-reviewer`
**ADRs:** ADR-0151 (implemented here), ADR-0077, ADR-0078, ADR-0074, ADR-0052/0053,
ADR-0108, ADR-0109, ADR-0144.

---

## 1. Where this lives — no bounded context

This is a **frontend-only** slice. No .NET project, no bounded context, no domain
model, no aggregate, no messaging, no persistence, no migration. The constitution's
§II primitive ban, the no-cross-context-reference rule, `Shared.Contracts`, Wolverine,
EF and Marten are all **not in play** — there is no entity, no value object, no
invariant and no domain→integration event to describe, and inventing one would be the
speculative generality ADR-0036 forbids.

The layering that *does* apply is ADR-0074's three-package split:

```
apps/shared/            ← the primitive changes here, and only here
  src/ui/primitives/
    Button.tsx          ← FR-001..FR-006  (the subject)
    Button.test.tsx     ← NEW             (the expectation, a different file)
apps/management-web/    ← two consumers adopt the prop
  src/features/overlays/OverlayEditorDialog.tsx
  src/features/layouts/LayoutEditorDialog.tsx
apps/kiosk-web/         ← untouched. Does not import Button.
docs/adr/0151-…         ← FR-008, a measured figure
e2e/                    ← untouched, and that is the point (spec §4 Q2)
```

**The dependency direction is one-way and already correct:** `management-web` imports
from `@smart-sentinel-eye/shared`; `shared` imports nothing from either app. Nothing in
this slice changes that, and no new package boundary is introduced.

## 2. The change to `Button.tsx`

Today (`5ea913dc`):

```tsx
export interface ButtonProps extends ComponentPropsWithRef<'button'> {
  variant?: ButtonVariant;
  asChild?: boolean;
}

export function Button({ variant = 'primary', asChild, className, type, ...rest }: ButtonProps) {
  …
  const base = '… disabled:pointer-events-none disabled:opacity-50 transition-colors';
  return (
    <Component
      type={asChild ? undefined : (type ?? 'button')}
      className={clsx(base, variants[variant], className)}
      {...rest}
    />
  );
}
```

Four edits, and nothing else:

1. **`unavailable?: boolean` on `ButtonProps`**, carrying the doc comment FR-006
   specifies. The doc comment is the substance of the change (ADR-0151 Implementation
   Notes); the code is four tokens.
2. **Destructure it** out of the props so it never reaches `...rest` (FR-004).
3. **`aria-disabled={unavailable}`** on `<Component>`, placed **before** `{...rest}` so
   a call site that still hand-rolls the attribute keeps winning — that ordering is
   what makes the adoption in §3 a strictly additive change rather than a race between
   two spellings.
4. **`clsx(base, variants[variant], unavailable !== undefined && 'aria-disabled:opacity-50', className)`**
   — caller `className` stays last, as today.

**Why `unavailable !== undefined` and not `unavailable &&`:** the class is a Tailwind
`aria-disabled:` variant, so it is inert unless the attribute is `"true"`. Gating it on
the *presence* of the prop rather than its truth keeps the rendered class list stable
across the `true`/`false` transition, which is what the two Save buttons do today
(their `className` is unconditional). Gating on truth would make the class list flicker
on every gate change for no benefit and would be a real, if invisible, behaviour
difference from the code being replaced.

**What deliberately does not change:** `disabled:pointer-events-none
disabled:opacity-50` stay in `base`. ADR-0151 considered stripping them ("the strongest
forcing function") and **rejected it** — removing them would make the 26 native sites
stop looking disabled, turning a latent inconsistency into a visible regression. FR-005
and the fourth Gherkin scenario pin that they stay.

**Types.** `ComponentPropsWithRef<'button'>` already carries `aria-disabled`, so
`unavailable` is a new name, not a narrowing. No call site's types change. No union or
`never`-branded prop (spec §4 Q1): it would break prop-spreading call sites and enforce
the wrong thing.

## 3. The change to the two consumers

Both dialogs, identically:

```diff
   <Button
     ref={saveRef}
     type="submit"
-    aria-disabled={saveBlocked}
-    className="aria-disabled:opacity-50 aria-disabled:cursor-progress"
+    unavailable={saveBlocked}
+    className="aria-disabled:cursor-progress"
   >
```

and the surrounding comment block updated to name the prop and ADR-0151 rather than
re-deriving the browser behaviour (the comment is what ADR-0151 says a named prop
replaces).

**Untouched, by requirement (FR-007):** `saveBlocked`'s definition, `handleFormSubmit`,
`saveRef`, `onSubmit`, and every `useRef`/`useEffect` around them. The form guard is the
only thing standing between this codebase and the implicit-submission trap, and it is
not in this slice's diff.

**Observable delta: none.** Attribute set identical (`aria-disabled` with the same
value, from the prop instead of the spread). Class set identical
(`aria-disabled:opacity-50` from the prop, `aria-disabled:cursor-progress` from the
call site) — **order differs**, which is why every characterisation assertion uses
`toHaveClass(a, b)` (order-independent) rather than an exact `className` string
comparison. An exact-string assertion here would be red for a reason that is not a
behaviour change, which is the definition of a bad characterisation test.

## 4. Test design — and how a reviewer sees each test discriminate

The criterion (spec 162, applied four times now): **can the assertion's subject change
without the assertion's text changing?** Every test below puts the expectation in a
different file from the subject, and every counterfactual is constructed in
`Button.tsx`.

### 4a-red — `apps/shared/src/ui/primitives/Button.test.tsx` (NEW, behaviour-changing)

Mirrors `ConfirmDialog.test.tsx` exactly: `// @vitest-environment jsdom` first line,
`@testing-library/react`, `afterEach(cleanup)`, `vi.fn()` for handlers.

| Test | Asserts | Red on `develop` because |
|---|---|---|
| unavailable emits `aria-disabled="true"`, never native `disabled` | attribute presence/absence | no such prop; it lands in `...rest` and React renders a stray `unavailable` attribute, so `aria-disabled` is absent |
| an unavailable Button stays focusable and its `onClick` still fires | `document.activeElement`, `vi.fn()` call count | same |
| `unavailable={false}` emits `aria-disabled="false"` | attribute value | same |
| no `unavailable` prop ⇒ no `aria-disabled`, no `aria-disabled:opacity-50`, base `disabled:*` classes intact | class + attribute | **green on `develop`** — this one is a *pin on what must not move*, and it is expected green. Stated here so a green result is not mistaken for a phase-4a failure |
| `unavailable` never reaches the DOM | `expect(el).not.toHaveAttribute('unavailable')` | red — today it does |

**Observing red is slightly non-obvious and the test-writer must not be surprised by
it:** vitest transpiles with esbuild and does **not** typecheck, so the file *runs*
against a `Button` that has no such prop and fails on assertions. `npm run typecheck`
will separately fail with "Property 'unavailable' does not exist" for the duration of
phase 4a. **That TypeScript error is not the red** — the verbatim vitest assertion
failures are, and those are what gets quoted in the PR (ADR-0139).

**Counterfactual the engineer runs and quotes (D1/D2):** in `Button.tsx`, swap
`aria-disabled={unavailable}` for `disabled={unavailable}`. The focusable test and the
onClick test go red; a native `disabled` element cannot be focused and swallows the
click. That is the ADR's whole claim, proved in the subject's own file.

### 4a-green — characterisation at the two call sites (behaviour-preserving)

Two assertions appended to suites that already exist and already render the exact state
needed:

- `apps/management-web/src/features/overlays/OverlayEditorDialogSaveGate.test.tsx`
  (`:237` already asserts `aria-disabled` is `'true'` — add `toHaveClass` there)
- `apps/management-web/src/features/layouts/LayoutEditorDialogSaveGate.test.tsx`
  (`:217`, same)

```
expect(saveButton).toHaveClass('aria-disabled:opacity-50', 'aria-disabled:cursor-progress');
expect(saveButton).not.toHaveAttribute('disabled');
```

**Captured GREEN on unmodified `develop` first** (both classes are on the hand-rolled
`className` today), then must pass **unmodified** after the conversion. An assertion
that has to be edited is evidence the behaviour moved — block, do not adjust
(ADR-0144).

**Counterfactual (D4):** drop `'aria-disabled:opacity-50'` from `Button.tsx`'s `clsx`
call. Both dialog tests go red. Subject in `apps/shared`, expectation in
`apps/management-web` — different packages, let alone different files.

**Counterfactual (D3):** in `Button.tsx`, emit `aria-disabled={unavailable || undefined}`.
The eight existing `toHaveAttribute('aria-disabled', 'false')` assertions across five
untouched suites go red. This is the strongest discrimination in the slice precisely
because nobody wrote those assertions for this change.

### Not written, and why

- **No new Playwright test** (spec §4 Q2). `e2e/overlays.spec.ts:426-451` already
  presses Enter in a text field in a real browser and counts PATCHes, and spec 160
  proved it discriminates at `Expected: 1, Received: 2`. It is not in the engineer's
  file list; a diff touching it is a review blocker.
- **No `user-event` Enter test.** ADR-0151 is explicit that such a test measures a
  synthetic click on the submit button. Writing one would add a green assertion that
  cannot fail for the reason it claims to test — exactly what spec 162 was about.
- **No test of the doc comment.** Memory: *guards that read the design artefact* — a
  test asserting prose contains a string proves the prose was written, not that the
  rule holds. FR-006 is a review item, not a test item.

## 5. Coverage and gates

ADR-0065's 90/80/90 gates are .NET coverage gates and do not bind these packages.
The frontend gates that do bind: `npm run lint`, `npm run typecheck`, `npm run test` per
workspace, `npm run test:e2e` in CI (ADR-0108), and `scripts/`'s test-collection guard
(memory: *tests that actually run*; `009b917a` made it fail loudly per app) — which the
new `Button.test.tsx` must satisfy by being collected, verified with
`npx vitest list --run`.

No gate is weakened, no test deleted, no suppression added, no analyzer narrowed
(ADR-0144's second prohibition).

## 6. Risks

| Risk | Why it is real | Mitigation |
|---|---|---|
| `aria-disabled="false"` silently becomes absent | eight assertions in five suites the engineer has no reason to open depend on it | FR-002; a dedicated `Button.test.tsx` case; counterfactual D3 |
| Class-order churn fails a characterisation test for a non-reason | `clsx` order changes by construction | order-independent `toHaveClass(a, b)`, never an exact `className` string |
| The form guard is "tidied" while the call site is edited | it sits ~70 lines above the button in the same file | FR-007 names it untouched; `e2e/overlays.spec.ts` unmodified is the backstop |
| The prop looks like it removes the need for a guard | it does not, and ADR-0151 §2 says so | FR-006's doc comment; §4 Q1's escalation says plainly that nothing couples them |
| Scope creep into the 26 native sites | the file list overlaps three of them (`OverlaysPage`, `LayoutsPage`, `ConfirmDialog` are neighbours) | §3 out-of-scope list; the phase-5 `git diff --stat` check |

## 7. Declarations (ADR-0144)

1. **Engineer: `frontend-engineer`.** Reviewer: `frontend-reviewer`. No backend, no
   infra, no Aspire resource, no CI change.
2. **New ADR required: no.** ADR-0151 is Accepted and §3 specifies this prop. The one
   question that would need an ADR — the runtime guard coupling — is **escalated as
   `[DECISION REQUIRED]` and not built**; the slice ships complete without it, so this
   is an escalation, not a BLOCK.
3. **Phase 4a colours, per work item:**
   - **`Button.tsx` + `Button.test.tsx` — BEHAVIOUR-CHANGING → RED.** Four of five new
     tests must be observed failing before `Button.tsx` is edited; the fifth (the
     no-prop pin) is expected green and is declared so in advance. Verbatim vitest
     output quoted in the PR.
   - **The two call-site conversions — BEHAVIOUR-PRESERVING → CHARACTERISATION,
     OBSERVED GREEN FIRST.** The two new `toHaveClass` assertions are captured green on
     unmodified `develop`, and must pass unmodified afterwards, alongside the five
     existing suites and `e2e/overlays.spec.ts`.
   - **ADR-0151 figure correction — documentation, no test.** The reproduction command
     is in spec §1.3 and in the ADR text itself; a reviewer runs it.
