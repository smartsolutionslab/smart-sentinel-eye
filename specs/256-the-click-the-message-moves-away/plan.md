# Plan 256 — The click the message moves away

`spec.md`. Issue [#2366](https://github.com/smartsolutionslab/smart-sentinel-eye/issues/2366).
Branch `fix/2366-mousedown-blur-submit-race`, worktree `D:/Github/sse-2366`, cut
from `origin/develop`.

## 1. Where it lives, and constitution / ADR alignment

- **Frontend only.** One production file:
  `apps/shared/src/ui/composites/OverlayGeometryFields.tsx`. No bounded context,
  no `Shared.Contracts`, no Domain, no AppHost resource, no migration. The
  constitution §II value-object rules bind backend Domain models only; nothing
  here touches them.
- **No new ADR** (spec header). No new dependency, no new test tooling: the
  component tests use the existing vitest + RTL + jsdom setup
  (`apps/shared/vitest.config.ts`, `// @vitest-environment jsdom` per file), the
  outcome tests use the existing Playwright `chromium` project against the
  Aspire stack (ADR-0103 — no stand-in harness).
- **Styling convention (ADR-0078), stated:** `OverlayGeometryFields.tsx` styles
  itself with inline style objects (`FIELD_ALERT_STYLE`, `FIELD_STATUS_STYLE`,
  …), not Tailwind classes. The change keeps that — mirroring the file, not
  converting it (ADR-0036, read before write).
- **§IV:** N/A — operator console only.

## 2. Fix shape — `OverlayGeometryFields.tsx`

### 2.1 One source for every message a slot can show

Today the strings are produced inline (`validate`, `commit`, `buildAdvisory`).
The reservation must list the same strings, so both read one source:

- `const NOT_A_NUMBER_MESSAGE = 'Enter a number.';` — used by `commit` in place
  of the literal.
- `function rangeMessage(spec: FieldSpec): string` — returns
  `` `${spec.label} must be between 0% and 100%.` `` for `position`,
  `` `${spec.label} must be greater than 0% and at most 100%.` `` for `size`.
  `validate` returns `rangeMessage(spec)` on refusal (the bound checks
  themselves are untouched).
- `function messagesFor(spec: FieldSpec): string[]` →
  `[NOT_A_NUMBER_MESSAGE, rangeMessage(spec)]`.
- The three advisory wordings become constants that `buildAdvisory` returns
  (logic untouched), collected as `ADVISORY_WORDINGS: string[]`.

Strings stay byte-identical — spec 151 FR-007–FR-012 and every existing test
depend on them.

### 2.2 A slot that is always as tall as its tallest message

A file-local component (not exported; one caller, two uses — no shared
abstraction until a second panel needs it):

```tsx
function ReservedMessageSlot({ candidates, textStyle, testId, children }) {
  return (
    <div data-testid={testId} style={{ display: 'grid' }}>
      {candidates.map((text) => (
        <span key={text} aria-hidden="true" style={{ ...textStyle, gridArea: '1 / 1', visibility: 'hidden' }}>
          {text}
        </span>
      ))}
      <div style={{ gridArea: '1 / 1' }}>{children}</div>
    </div>
  );
}
```

Every child sits in the same grid cell, so the cell's height is the tallest
candidate *at the rendered width* — the width-independence spec §3.1 requires.
`visibility: hidden` keeps the copies laid out but unpainted and
non-interactive; `aria-hidden="true"` keeps them out of the accessibility tree
(visibility-hidden content is already excluded by browsers; the attribute
makes it explicit and is what the component test asserts). The copies carry the
**same text style** as the live message (`FIELD_ALERT_STYLE` /
`FIELD_STATUS_STYLE`), or the heights would not match.

### 2.3 Using it

- **Per field** (`:229-233`): the existing conditionally mounted
  `<span id={errorId} role="alert" style={FIELD_ALERT_STYLE}>` moves, unchanged,
  inside `<ReservedMessageSlot candidates={messagesFor(spec)}
  textStyle={FIELD_ALERT_STYLE} testId={`overlay-geometry-message-slot-${spec.field}`}>`.
  The slot is always rendered; only the alert inside it mounts and unmounts.
  Consequences kept deliberately: `role="alert"` is still inserted with its
  content (today's announcement behaviour); `aria-describedby` still points at
  `errorId` only while an error exists (FR-017 test at
  `OverlayGeometryFields.test.tsx:480`). `committedValueError`'s messages come
  from `validate`, so they are already in `messagesFor`.
- **Advisory** (`:245-247`): the always-mounted
  `<span role="status" data-testid="overlay-geometry-advisory">` moves, unchanged,
  inside `<ReservedMessageSlot candidates={ADVISORY_WORDINGS}
  textStyle={FIELD_STATUS_STYLE} testId="overlay-geometry-advisory-slot">`. It
  stays mounted with `''` when clear (should-fix 4 of spec 151's review).

The slot wrapper is the new grid item in both parents (the field's flex column,
`gap: 4`; the panel's grid, `gap: 12`), and is present from first render — so
those gaps, too, stop appearing with the first message.

### 2.4 Explicitly not changed

- Commit timing (blur / Enter / Escape, FR-004) and the `onCommit` payload —
  spec §3.2 explains why moving it does not help.
- `FormField.tsx` and every RHF dialog (spec §2.2).
- `Dialog.tsx` (spec §3.3). `OverlayEditor.tsx`, `OverlayEditorDialog.tsx`.

## 3. Entities, messaging, boundaries

None. No domain entity, value object, domain or integration event, or
cross-context reference is involved. The only boundary rule in play is the
shared-package one: `apps/shared` is consumed by `apps/management-web`; the
change is internal to one shared composite and alters no exported type or prop.

## 4. Tests

### 4.1 Outcome — Playwright, `e2e/overlays.spec.ts` (RED before the fix)

jsdom does no layout, so only a real browser can observe the defect. Add to the
existing file, reusing `signInAsOperator`, `FIRST_WRITE_TEST_TIMEOUT_MS` /
`FIRST_WRITE_TIMEOUT_MS`, the `E2E ` disposable-name prefix (teardown pattern,
`overlays.spec.ts:116-118`) and the gateway read-back at `:79-101,125-142`:

1. **Refused draft, one click** — New overlay, fill name, `fill('0')` into
   `getByLabel('Width', { exact: true })` (focus stays), one
   `getByRole('button', { name: /save as draft/i }).click()`; expect the name
   visible in the list within `FIRST_WRITE_TIMEOUT_MS`; read back
   `normalizedWidth === 0.3`.
2. **Valid off-edge commit, one click** — same with Left `90`; read back
   `normalizedX === 0.9`. Proves the blur commit still reaches the payload
   before submit.
3. **Position does not move (refusal)** — fill Width `0`; record
   `saveButton.boundingBox()`; `widthField.blur()`; expect the refusal
   `role="alert"` visible; expect the box's `y` unchanged (`toBe`, exact).
4. **Position does not move (advisory)** — record the box; fill Left `90` +
   blur; assert `y` unchanged and the advisory contains "clipped"; fill Left
   `10` + blur; assert `y` unchanged and the advisory empty. Cancel the dialog
   (nothing saved).

Playwright's `click()` waits for stability, then presses and releases at one
point — the exact pair the defect needs, so (1) and (2) reproduce the bug
rather than approximate it. The refusal in (1) must be a **size** message: it
wraps to several lines, a shift far larger than the button's half-height, so
the red is not marginal. Expected red: (1)/(2) time out waiting for the name in
the list; (3)/(4) report differing `y`.

**Stack contention:** these need the Aspire stack. One machine, one stack —
the orchestrator must confirm no other worktree's AppHost is running before the
test-writer boots it.

### 4.2 Mechanism — vitest, `OverlayGeometryFields.test.tsx` (RED before the fix)

New `describe` block, existing file, existing helpers (`field(label)`,
`buildLabel`, `ControlledFields`), sentence-style names:

- *Every message a field can show is reserved before any is shown* — for each
  of Left / Top / Width / Height, `getByTestId('overlay-geometry-message-slot-<field>')`
  contains `[aria-hidden="true"]` elements whose `textContent` set equals the
  literal expected set (`'Enter a number.'` + the FR-008/FR-009 sentence with
  that field's label). Literals written in the test, not imported from the
  component — the assertion must not check its own input.
- *Every advisory wording is reserved before any is shown* — same for
  `overlay-geometry-advisory-slot` and the three FR-012 sentences.
- *The reserved copies are invisible to assistive technology* — with nothing
  refused, `queryAllByRole('alert')` is empty and
  `getByRole('status').textContent === ''`; after a refusal, exactly one alert.
- *A refusal adds no box to the field's column* — the Width field's column
  (`field('Width').closest('label')!.parentElement`, found without the new test
  id so the assertion does not depend on the fix's own markup) has the same direct-child count before and after
  `fireEvent.change` to `'0'` + `fireEvent.blur`, while an alert did appear.
  Red today: the alert span is inserted as a new direct child.

Expected red: `getByTestId` throws (slots do not exist).

### 4.3 Regression net — must pass unmodified

`OverlayGeometryFields.test.tsx` (existing cases), `OverlayEditor*.test.tsx`,
`OverlayLabel*.test.tsx`, `apps/management-web/src/features/overlays/*.test.tsx`,
and the existing `e2e/overlays.spec.ts` cases. Captured green before the fix
(as part of 4a's run) and green after with zero edits.

### 4.4 Commands

```sh
pnpm --filter @smart-sentinel-eye/shared test -- OverlayGeometryFields
pnpm --filter @smart-sentinel-eye/shared test
pnpm --filter @smart-sentinel-eye/management-web test
pnpm exec playwright test e2e/overlays.spec.ts --project=chromium
pnpm lint && pnpm typecheck && pnpm format:check   # repo root scripts
# typecheck:e2e may fail on a clean develop (missing @types/node at the root) —
# stash and re-run on develop before treating it as this branch's fault.
```

## 5. Risks

| Risk | Mitigation |
|---|---|
| Hidden copies styled differently from the live message → reserved height ≠ real height, race survives at some widths | Same style object passed to both; e2e (3)/(4) assert exact `y` equality in real Chromium |
| A candidate list drifts from the messages `commit`/`validate` actually produce | §2.1 single source; 4.2's literal sets fail if a string changes on one side only |
| Hidden text picked up by a text query (`getByText`) in some test | Searched: no test queries these strings by text; all use role / `textContent` / test id |
| Permanent blank band looks like a layout bug | Stated cost (spec §3.1); verification note includes a screenshot of the dialog at rest |
| E2E stack contention with sibling worktrees | §4.1 stack note |
