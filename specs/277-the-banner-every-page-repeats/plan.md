# Plan 277: The banner every page repeats

**Spec:** `spec.md` · **Issue:** #2523 · **Lane:** supervised (ADR-0037)

## 0. Shape of the change

Pure frontend, `management-web` + `apps/shared`. It adds one new composite and its test, and
edits seven existing page files to call it instead of inlining the fragment. No backend, no
contract, no migration, no domain concept.

| Area | Files it may change | Agent (phase 4b) |
|---|---|---|
| New composite | `apps/shared/src/ui/composites/RetryBanner.tsx`, `RetryBanner.test.tsx` | `frontend-engineer` |
| Six replace-shaped call sites | `CamerasPage.tsx`, `AuditPage.tsx`, `LayoutsPage.tsx`, `OverlaysPage.tsx`, `SystemVariablesPage.tsx`, `WallsPage.tsx` (+ new `WallsPage.test.tsx`) | `frontend-engineer` |
| One stale-shaped call site | `CameraDetailPage.tsx` | `frontend-engineer` |

**Any other file in the diff is a finding** — it means the swap needed more than a call-site
change, and spec §4 FR-006 says what happens then (stop, report, don't adjust).

## Constitution / ADR check

| Rule | How this plan satisfies it |
|---|---|
| ADR-0037 phases | Phases 1–3 here, supervised lane — each gate is a human stop, not an autonomous continue. |
| ADR-0036 (Karpathy: smallest change, no speculative generality) | One new component, two props, no variant enum (spec §5 A2). The six same-shape-but-different-behaviour sites (spec §1.3) are explicitly left alone. |
| Constitution §Testing / ADR-0139 | Behaviour-preserving → characterisation, observed green (spec §6). No new production behaviour, so no red test is expected anywhere except the one genuinely new test file (`RetryBanner.test.tsx`, which characterises nothing because there is nothing to characterise — it pins the new component's own contract). |
| ADR-0074 | React + TypeScript + Vite, `management-web` + `apps/shared`. No `kiosk-web` change (spec §7). |
| ADR-0077 / ADR-0078 | The composite uses the same Tailwind class string and token-driven colours (`accent-fault`) every call site already used. No new Radix primitive is introduced — a `role="alert"` div and a plain `<button>` needed no headless primitive before, and still don't. |
| ADR-0109 (contention files) | Every call-site file has exactly one writer in this change (the `frontend-engineer`), and each site's edit is disjoint from every other site's — see tasks.md fan-out. |
| §III boundaries | No cross-context reference. `apps/shared` already exports composites to both apps; NetArchTest is backend-only and untouched. |
| §IV latency | N/A (spec header) — operator console only. |

## 1. Why a caller-supplied `message`, not a `variant` prop

The issue frames the six-vs-one split as "replace" vs "stale" and asks the extraction not to
flatten it into one sentence. Two designs satisfy that:

**(a) `variant: 'replace' | 'stale'`, with the component owning both sentences' *styling*
differences.** Rejected: there is no styling difference to own. All seven sites use the identical
`role="alert"` div, identical class string, identical button. A `variant` prop here would exist
only to be read back for documentation, never to drive a branch in the component — exactly the
"config knob for a need that doesn't exist yet" ADR-0036 rules out.

**(b) A `message: string` prop, caller-supplied, no variant.** Adopted. Each of the seven call
sites already has its own sentence in its own source file; the composite receiving that text
verbatim is what "must not flatten the difference into one sentence" *means* in a component with
no other axis of variation. The "replace" framing versus the "stale" framing lives entirely in
the words ("Could not load cameras." vs "Could not refresh this camera — what you see may be out
of date."), which the caller — not the composite — has always owned.

If a future spec needs the *rendering*, not just the wording, to differ by flavour (a different
icon, a different placement relative to surrounding content), that is new information this spec
does not have, and the prop is added then, against a real second call site that needs it — not
speculatively now.

## 2. `RetryBanner` design

```tsx
// apps/shared/src/ui/composites/RetryBanner.tsx
export interface RetryBannerProps {
  /** The sentence explaining what failed. Caller-supplied, not templated —
   *  a list page says its content didn't load; CameraDetailPage says the
   *  record shown may be stale. Folding these into one shared string would
   *  erase a distinction operators rely on (issue #2523). */
  message: string;
  /** Called when the operator activates Retry. Callers pass their own
   *  `() => void refetch()`, unchanged from what they called directly before. */
  onRetry: () => void;
}

export function RetryBanner({ message, onRetry }: RetryBannerProps) {
  return (
    <div
      role="alert"
      className="mb-4 rounded-md border border-accent-fault/40 bg-accent-fault/10 px-3 py-2 text-sm text-accent-fault"
    >
      {message}{' '}
      <button type="button" className="underline" onClick={onRetry}>
        Retry
      </button>
    </div>
  );
}
```

Notes:

- `onRetry` is typed `() => void`, and every call site keeps wrapping its own `refetch()` in
  `() => void refetch()` at the call site — identical to what each page does today. `RetryBanner`
  does not import or know about RTK Query.
- No default export, matching `DataTable` / `FormErrorSummary` / `ChainRecoveryNotice`.
- No barrel file. Imported as `@smart-sentinel-eye/shared/ui/composites/RetryBanner`, mirroring
  `@smart-sentinel-eye/shared/ui/composites/DataTable`.
- `mb-4` stays baked into the component: all seven current sites want it (it's the first element
  after a page header, or the first content inside CameraDetailPage's error slot), and no call
  site needs the alternative of no margin. If a future site needs a marginless banner, that's a
  new prop added against that real need.

## 3. Call-site edits (each is a two-line diff: the import, and the JSX block)

| Site | Before (today) | After |
|---|---|---|
| `CamerasPage.tsx:123-131` | inline `<div role="alert" …>Could not load cameras.…</div>` | `<RetryBanner message="Could not load cameras." onRetry={() => void refetch()} />` |
| `AuditPage.tsx:165-173` | inline, "Could not load the audit trail." | `<RetryBanner message="Could not load the audit trail." onRetry={() => void refetch()} />` |
| `LayoutsPage.tsx:118-127` | inline, "Could not load layouts." (the `error` banner only — **not** the `mutationError` banner at :130-146, which stays inline; see spec §1.3) | `<RetryBanner message="Could not load layouts." onRetry={() => void refetch()} />` |
| `OverlaysPage.tsx:110-119` | inline, "Could not load overlays." (the `error` banner only) | `<RetryBanner message="Could not load overlays." onRetry={() => void refetch()} />` |
| `SystemVariablesPage.tsx:103-112` | inline, "Could not load variables." (the `error` banner only) | `<RetryBanner message="Could not load variables." onRetry={() => void refetch()} />` |
| `WallsPage.tsx:34-43` | inline, "Could not load walls." | `<RetryBanner message="Could not load walls." onRetry={() => void refetch()} />` |
| `CameraDetailPage.tsx:128-137` | inline, "Could not refresh this camera — what you see may be out of date." | `<RetryBanner message="Could not refresh this camera — what you see may be out of date." onRetry={() => void refetch()} />` |

Each site keeps its own `error !== undefined &&` (or equivalent) guard around the new element —
that guard is the page's own data-fetch state, not something `RetryBanner` should own.

## 4. Test strategy detail

- **Six sites already have a covering test** (each page's "Shows a retry control when the list
  query fails", `CameraDetailPage.test.tsx`'s "Retries the refresh when the operator presses
  Retry"). These assert `getByRole('alert')` / `getByRole('button', { name: /retry/i })` and that
  clicking calls `refetch`. Because `RetryBanner` renders identical roles, text and click wiring,
  these tests need **no edit** — they were written against the DOM shape, not the fact that the
  shape happened to be inlined.
- **`WallsPage.tsx` has none.** `tasks.md` T2f adds `WallsPage.test.tsx` with the same
  "Shows a retry control when the list query fails" case the other five list pages already have
  (same mock shape: `data`, `isLoading`, `isFetching`, `error`, `refetch`), captured passing on
  the pre-change inline banner **before** T3f swaps it to `RetryBanner`.
- **`RetryBanner.test.tsx`** (new) asserts, standalone: the role, the exact class string, that
  `message` renders verbatim, and that clicking "Retry" calls `onRetry` exactly once with no
  arguments.

## 5. Risks

| Risk | Mitigation |
|---|---|
| A page's test queries the inline `<div>` by something more specific than role/text (e.g. a snapshot) | Read each test before editing its page (tasks.md 4a); if a snapshot exists, it is expected to differ only in a wrapper element, which still counts as "moved" under FR-006 and blocks — checked in T-verify below, not assumed away here. |
| `WallsPage.tsx`'s missing test is a symptom of something bigger (e.g. the whole file is genuinely uncovered) | Scope stays narrow: only the retry-banner behaviour gets a new test. No attempt to bring the whole file to parity with its siblings. |
| Widening scope to a 7th, un-named site (WallsPage) is disputed at phase 6 review | Spec §5 A1 already marks it droppable as its own task/commit; nothing else depends on it. |
| `RulesPage.tsx`'s two near-miss banners get swept in by habit during implementation | Spec §1.3 names them explicitly as out of scope; tasks.md does not include a task that touches `RulesPage.tsx` or `WallDetailPage.tsx`. |

## 6. Phase 5 (verify) expectations

- `pnpm --filter @smart-sentinel-eye/shared exec vitest run src/ui/composites/RetryBanner.test.tsx`
  green.
- Each of the seven pages' retry test green, and `git diff origin/develop -- '**/*.test.tsx'`
  shows only the **addition** of `WallsPage.test.tsx` — no existing test file's content differs
  (the new file is new lines; nothing pre-existing is edited).
- `pnpm lint`, `pnpm typecheck`, `pnpm format:check`, `pnpm build` green.
- Manual: boot the stack, visit each of the seven pages/records with the query forced to fail
  (DevTools request blocking or a stopped dependency), confirm the banner text and Retry button
  are unchanged from before the refactor, and that CameraDetailPage still shows the cached record
  beneath its banner.

## 7. Board

Issue #2523 is feature-level. It should be added to Project #13 by hand
(`gh project item-add 13 --owner smartsolutionslab --url <issue-url>`) — the orchestrator's job,
not this phase's (CLAUDE.md, Phase 3).
