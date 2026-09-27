# Spec 274: The banner every page repeats

**Issue**: #2523 (feature-level; no per-task issues, CLAUDE.md Phase 3)
**Lane**: supervised (ADR-0037). Phases 1–3 only in this artifact set.
**ADRs**: No ADR governs a shared-composite obligation directly — this is a refactor under the
existing composite pattern already used by `DataTable`, `ChainRecoveryNotice` and
`FormErrorSummary` (`apps/shared/src/ui/composites/`). It must follow: **ADR-0074** (two React
apps, `management-web` + `apps/shared`), **ADR-0077** (Radix + Tailwind UI conventions),
**ADR-0078** (Tailwind design tokens), **ADR-0036** (smallest change; no speculative
generality), **ADR-0139** / constitution §Testing (this is behaviour-preserving, so
characterisation, not red — see §6), **ADR-0109** (disjoint files enable parallel fan-out).
**New ADR needed**: No. Extracting a repeated presentational fragment into
`apps/shared/src/ui/composites/` is the established pattern (see the three composites above);
nothing about locked tech, a boundary, or a cross-cutting policy changes.
**Spec number**: 274. Checked 2026-09-27 against every `specs/` directory in every local
worktree (`smart-sentinel-eye`, `sse-2200`, `sse-2324`, `sse-2332`, `sse-2337`, `sse-2358`,
`sse-2365`, `sse-2450`, `sse-2490`, `sse-2521`, `sse-2523`, `sse-2526`, `sse-2570`, `sse-2579`,
`sse-2582`, `sse-2586`, `sse-2589`, `sse-2632` — highest found: 273, in `sse-2632`) and every
`origin/*` branch not checked out as a worktree (highest found: 272, on the two `chore/2638-*`
branches). No branch or worktree holds 274. **Re-check before opening the PR** (memory: *spec
number: origin/develop isn't enough*).
**Latency budget (constitution §IV)**: **N/A.** Operator console only (`management-web` +
`apps/shared`). Nothing on the event→overlay path, nothing in `kiosk-web`.

## 1. The premise, re-checked against `1a51bd18` (this worktree's tip)

The issue's inventory is **mostly accurate, with three corrections**: two line numbers had
drifted, one named site was already shipped as described, and **one identical site exists that
the issue does not name** — the count is seven, not six.

| # | Site | File:line (current) | Issue said | Sentence | Shape |
|---|---|---|---|---|---|
| 1 | Cameras list | `apps/management-web/src/features/cameras/CamerasPage.tsx:123` | `:123` (match) | "Could not load cameras." | replace |
| 2 | Audit list | `apps/management-web/src/features/audit/AuditPage.tsx:165` | `:165` (match) | "Could not load the audit trail." | replace |
| 3 | Layouts list | `apps/management-web/src/features/layouts/LayoutsPage.tsx:118` | `:112` (drifted +6) | "Could not load layouts." | replace |
| 4 | Overlays list | `apps/management-web/src/features/overlays/OverlaysPage.tsx:110` | `:110` (match) | "Could not load overlays." | replace |
| 5 | System variables list | `apps/management-web/src/features/systemVariables/SystemVariablesPage.tsx:103` | `:98` (drifted +5) | "Could not load variables." | replace |
| 6 | Walls list — **not in the issue** | `apps/management-web/src/features/walls/WallsPage.tsx:34` | *(absent)* | "Could not load walls." | replace |
| 7 | Camera detail | `apps/management-web/src/features/cameras/CameraDetailPage.tsx:128` | *(added by #2432)* | "Could not refresh this camera — what you see may be out of date." | stale |

Every one of these seven is byte-for-byte the same DOM shape: a `role="alert"` `<div
className="mb-4 rounded-md border border-accent-fault/40 bg-accent-fault/10 px-3 py-2 text-sm
text-accent-fault">`, a sentence, `{' '}`, and `<button type="button" className="underline"
onClick={() => void refetch()}>Retry</button>`.

### 1.1 #2432 / CameraDetailPage: shipped, not pending

`gh issue view 2432` returns **CLOSED**. Its fix landed in **PR #2525** ("fix(management-web):
a failed refresh no longer denies a camera that's in hand"), **MERGED**. `CameraDetailPage.tsx`
exists in this worktree today with the banner at line 128, sentence "Could not refresh this
camera — what you see may be out of date." — exactly the "stale" wording the issue's note
describes. This means site 7 is not hypothetical or future work: it exists now, and this spec's
scope covers it as delivered code, same as the other six.

### 1.2 The seventh site: `WallsPage.tsx:34`, found by grep, not named in the issue

`grep -rn "border-accent-fault/40"` across `apps/management-web` and `apps/shared` turns up ten
files. Nine were inspected for shape; three (`WallsPage.tsx`, `WallDetailPage.tsx`,
`RulesPage.tsx`) are outside the issue's six-file list. Of those three, exactly one —
`WallsPage.tsx:34-43` — matches the identical-shape criterion: same class string, sentence
"Could not load walls.", `<button className="underline" onClick={() => void refetch()}>Retry`.
It is included here (site 6 above) because leaving a byte-identical copy of the fragment
sitting beside the newly-extracted composite would defeat the purpose of extracting it, and
because "no shortcuts" (CLAUDE.md) argues against knowingly walking past a duplicate this spec's
own reasoning already condemns. **This widens the issue's stated scope by one file.** If a
reviewer judges that widening out of bounds for #2523 specifically, dropping US-Walls
(plan §2, task T5) leaves the other six independently shippable — nothing else depends on it.

**`WallsPage.tsx` has no test file at all** (`WallsPage.test.tsx` does not exist, confirmed by
directory listing). Constitution §Testing's characterisation rule ("a refactor with no covering
test is a rewrite") applies: a covering test for this site must be **written first**, captured
green on `develop`, before the refactor touches it (tasks.md T2f).

### 1.3 Same-shape sites outside scope (reported, not converted)

These carry the same box styling but differ in a way that is not "only the sentence differs" —
each has a different *behaviour*, not just different words, so folding it into `RetryBanner`
would either lose behaviour or force a speculative prop this spec has no call site to justify
(ADR-0036).

| File:line | What it is | Why it's a different shape |
|---|---|---|
| `WallDetailPage.tsx:139-145` | `{backendError}` in the same box, **no button** | Not a retry banner — no retry action exists at this call site; it's a plain error display |
| `LayoutsPage.tsx:130-146`, `OverlaysPage.tsx:122-138`, `SystemVariablesPage.tsx:115-127` | mutation-error banner (`mutationError !== undefined`) | Text comes from `problemDetail(...)` (dynamic, conflict-aware), the button is **conditional** (`isConflict(mutationError) &&`), and it says **"Reload"**, not "Retry" — reloading a stale-conflict intent is a different operation from retrying a failed read |
| `RulesPage.tsx:149-166` | the same mutation-error shape as above | same reason |
| `RulesPage.tsx:168-174` | `isError` / "Could not load rules." | Different className (no `mb-4`, no `bg-accent-fault/10`), wraps text in `<p>`, and uses the shared `Button` primitive (`variant="secondary"`) instead of a raw `<button>` — a different implementation, not a drifted copy of this one |

None of these six sites is touched by this spec. If a future spec wants a "conflict/reload"
composite, it is a new piece of work with its own call sites and its own design — not an
extension bolted onto `RetryBanner` for needs this spec doesn't have (ADR-0036, "no speculative
generality").

## 2. User stories

### US1 (P1): A `RetryBanner` composite exists in `apps/shared`

A developer imports `RetryBanner` from `apps/shared/src/ui/composites/RetryBanner.tsx` the same
way `CamerasPage.tsx` already imports `DataTable`. It renders the identical DOM (role, classes,
button, "Retry" label) that all seven sites render today, parameterised only by the sentence and
the retry callback.

**Independent test**: `RetryBanner.test.tsx` renders the composite standalone and asserts the
role, class list, sentence and that clicking Retry calls the supplied callback once.

### US2 (P1): The six replace-shaped list pages call it and render unchanged output

`CamerasPage`, `AuditPage`, `LayoutsPage`, `OverlaysPage`, `SystemVariablesPage` and `WallsPage`
each replace their inline banner with `<RetryBanner message="…" onRetry={() => void refetch()}
/>`. Each page's own existing "Shows a retry control when the list query fails" test (or, for
`WallsPage`, a newly-written equivalent — §1.2) passes **unmodified** in wording and assertions.

**Independent test**: each page's existing (or, for Walls, newly added) retry test, run before
and after, byte-identical.

### US3 (P1): The stale-data camera-detail page calls it without losing the distinction

`CameraDetailPage` replaces its inline banner with `<RetryBanner message="Could not refresh this
camera — what you see may be out of date." onRetry={() => void refetch()} />`. The record below
the banner keeps rendering exactly as it does today — `RetryBanner` does not gain an opinion
about what surrounds it.

**Independent test**: `CameraDetailPage.test.tsx`'s existing "Retries the refresh when the
operator presses Retry" test passes unmodified, and the record's fields are still asserted
present alongside the alert.

All three stories touch disjoint files except the shared `RetryBanner.tsx` itself (US1), which
every one of US2/US3's edits depends on but none of them modifies.

## 3. Acceptance scenarios

```gherkin
Feature: One RetryBanner composite replaces seven inline copies, output unchanged

  Scenario: The composite renders the same DOM the inline copies rendered
    Given RetryBanner is given a message and an onRetry callback
    Then it renders a role="alert" element with the exact class string
      "mb-4 rounded-md border border-accent-fault/40 bg-accent-fault/10 px-3 py-2 text-sm text-accent-fault"
    And it renders the message, then an underlined "Retry" button

  Scenario: Clicking Retry calls the callback, not a hardcoded refetch
    Given RetryBanner is mounted with a spy as onRetry
    When the operator clicks Retry
    Then the spy is called exactly once, with no arguments

  Scenario: Each of the seven call sites keeps its own sentence
    Given CamerasPage and CameraDetailPage both render a failed query
    Then CamerasPage's banner reads "Could not load cameras."
    And CameraDetailPage's banner reads "Could not refresh this camera — what you see may be out of date."
    And the two sentences are not merged into one shared string

  # --- Conflict: behaviour must not move ---
  Scenario: A page's retry test is unmodified after the swap
    Given a page's existing "Shows a retry control when the list query fails" test
    When the inline banner is replaced by RetryBanner
    Then the test file's assertions are byte-identical to before the change
    And the test still passes

  # --- Bad request: WallsPage had no covering test ---
  Scenario: A site with no prior test gets one before it is touched
    Given WallsPage.tsx has no test file today
    When a characterisation test for its retry banner is written
    Then it is captured passing on the pre-change code
    Then WallsPage is refactored to use RetryBanner
    And the same test passes unmodified after

  # --- The distinction the issue calls out ---
  Scenario: CameraDetailPage's stale-data framing survives the extraction
    Given CameraDetailPage shows a cached record and a failed refresh
    Then the record's fields (name, address controls, back link) still render
    And the banner text still says the shown data may be stale, not that nothing loaded
```

**Auth scenario: N/A, with the reason.** No endpoint, scope, or request changes. `onRetry` calls
the same `refetch()` each page already calls; RetryBanner sends no request of its own.

## 4. Functional requirements

- **FR-001** `RetryBanner` (`apps/shared/src/ui/composites/RetryBanner.tsx`) accepts a `message`
  (the sentence, caller-supplied) and an `onRetry` callback, and renders the exact DOM the seven
  sites render today.
- **FR-002** The six replace-shaped sites (Cameras, Audit, Layouts, Overlays, System variables,
  Walls) and the one stale-shaped site (CameraDetailPage) all use `RetryBanner`. No inline copy
  of the class string `border-accent-fault/40` combined with a `role="alert"` + Retry button
  remains at any of the seven.
- **FR-003** The six sites listed in §1.3 are **not** touched. `RetryBanner` gains no prop or
  variant that only they would use.
- **FR-004** Every pre-existing test covering one of the seven sites passes **unmodified**
  (constitution §Testing; see §6 for the one exception that needs a test written first).
- **FR-005** The distinction the issue calls out — "replace" (five list pages plus Walls) versus
  "stale" (CameraDetailPage) — is preserved through the caller-supplied `message` text, not
  flattened into one shared sentence. See plan.md §2 for why this needs no separate `variant`
  prop.
- **FR-006** No analyzer, lint rule or test is suppressed, relaxed or edited to make the swap
  pass. An assertion that has to change means the DOM moved: stop and report, don't adjust
  (ADR-0144 applies even in the supervised lane, by the same reasoning).

## 5. Assumptions and judgement calls (marked, not buried)

- **[A1] `WallsPage.tsx` (site 6) is in scope**, though the issue does not name it. See §1.2 for
  the reasoning and the drop-if-disputed note. If dropped, renumber nothing else — it is its own
  task and its own commit (tasks.md).
- **[A2] No `variant` prop.** The issue's own text ("Only the sentence differs" for the five, and
  a *separate* note about CameraDetailPage) reads as license for a `variant: 'replace' | 'stale'`
  enum. This spec rejects that: the two flavours render byte-identical DOM — same classes, same
  button, same role — and differ **only** in the words passed as `message`. A prop that drives no
  branch in the component is decoration, not design (ADR-0036's "no config knob for a need that
  doesn't exist yet"). If a future site needs the DOM itself to differ by flavour, that is new
  information and a new prop, added then.
- **[A3] "Retry" is not a prop.** All seven sites say "Retry" verbatim. No call site needs a
  different label, so the button text is fixed inside the composite, not parameterised.
- **[A4] No barrel export.** `apps/shared/src/ui/composites/` has no `index.ts` (unlike
  `primitives/`, which does). `RetryBanner` is imported by its own path, matching how
  `DataTable`, `ChainRecoveryNotice` and `FormErrorSummary` are already imported.

## 6. Phase 4a colour

**CHARACTERISATION (behaviour-preserving), observed green — not red.** This is a refactor: the
same seven banners render, with the same text, the same class list, and the same click behaviour,
before and after. No new user-visible behaviour is introduced.

- For the six sites that already have a covering test (Cameras, Audit, Layouts, Overlays, System
  variables, CameraDetailPage), that existing test is captured **passing on `develop` before**
  the change and must pass **unmodified** after (tasks.md, phase 4a).
- For `WallsPage.tsx` (§1.2), which has no covering test today, one is **written first**,
  captured passing on the pre-change code, then must also pass unmodified after the refactor
  (constitution §Testing: "a refactor with no covering test is a rewrite").
- `RetryBanner.test.tsx` itself is new-file, new-behaviour test-writing for a new component —
  but the component has no existing behaviour to preserve, so there is nothing to characterise;
  it is written to lock down the DOM shape the seven call sites are being pointed at, verified by
  the fact that swapping each call site over changes nothing its own tests observe.
- An assertion that has to be **edited** to pass after the swap is evidence the rendered output
  moved. That blocks the change; it is not adjusted (issue #2523's own instruction, echoed in
  constitution §Testing).

## 7. Out of scope

- The six sites in §1.3 (mutation-error / conflict banners, `WallDetailPage`'s static banner,
  `RulesPage`'s Button-based load-error banner).
- Any change to `refetch()` semantics, RTK Query configuration, or the underlying queries.
- A `variant` prop or any other generalisation not driven by one of the seven call sites (§5 A2).
- Adding `RetryBanner` to `kiosk-web` — nothing there renders this shape today.
