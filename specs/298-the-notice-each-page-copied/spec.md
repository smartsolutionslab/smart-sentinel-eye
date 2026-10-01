# Spec 298 — The notice each page copied

**Issue:** [#2693](https://github.com/smartsolutionslab/smart-sentinel-eye/issues/2693)
— *Five console pages hand-roll RetryBanner's fault box on call-site alpha*. Labels `tech-debt`,
`agent:ready`. Filed from spec 297 §3.2 (#2635).
**Branch:** `refactor/2693-retrybanner-consolidation` (cut from `origin/develop` @ `56f2cde8`)
**Created:** 2026-10-01
**Lane:** autonomous (ADR-0144)

**Spec number.** On 2026-10-01 every remote branch and every worktree (`sse-2324`, `sse-2677`,
`sse-2692`, `sse-2698`, the main checkout) was listed: `origin/develop` tops out at **297**,
**296** is claimed by a remote branch, nothing claims **298**. Re-check before opening the PR —
`sse-2692` and `sse-2698` are live and may claim a number first (memory: *spec number:
origin/develop isn't enough*).

**ADRs this spec is bound by (it applies them; it decides nothing they left open):**

- **ADR-0148** — two-layer OKLCH tokens: components cite semantic roles only. The fault notice
  cites `--color-accent-fault-subtle` / `--color-accent-fault-border`, which exist for exactly
  this box (issue #2523, `tokens.css:139-149`).
- **ADR-0077** / **ADR-0078** — the design system: shared composites in
  `apps/shared/src/ui/composites`, Tailwind classes over token custom properties.
- **ADR-0036** — smallest change; no speculative props. The basis for every "not here" in §3.2
  and for the component decision in §4.
- **ADR-0139 / ADR-0144** — red first; phase-4a colour declared per site in §6.
- **ADR-0150** — every new test waits on a condition, not a count.
- **ADR-0037**, **ADR-0109** (`[P]`), **ADR-0053** (sentence-style test names).

**No new ADR is needed** (§5).

---

## 1. The issue's premise, re-checked on this tree (`56f2cde8`)

| Issue says | Tree says | Consequence |
|---|---|---|
| Five sites carry `border-accent-fault/40 bg-accent-fault/10 … text-accent-fault`: `WallDetailPage.tsx:143`, `RulesPage.tsx:152`, `OverlaysPage.tsx:116`, `SystemVariablesPage.tsx:109`, `LayoutsPage.tsx:124`. | Four line numbers hold exactly. **`RulesPage` has drifted by one**: the box's `className` is at `:153` (its `<div` at `:151`). | None beyond citing `:153`. |
| A border-only variant at `RulesPage.tsx:169`. | It is at **`:170`** (`<div role="alert" className="space-y-2 rounded-md border border-accent-fault/40 p-3 text-sm">`). It has **no fill and no fault text colour**, and its Retry is the `Button` primitive (`variant="secondary"`), not a link. | It is the **only** genuine retry among the six and becomes `RetryBanner` (US2) — a visible change (gains the tint, fault text, link-style Retry), declared. |
| These are "look-alikes of `RetryBanner`", which "each should either render `RetryBanner` (if it offers a retry) or a shared fault notice". | **Five of the six offer no retry.** Four (`Layouts`, `Overlays`, `SystemVariables`, `Rules:153`) render a **mutation refusal** — `problemDetail(mutationError, isStaleConflict ? CONFLICT_FALLBACK : 'Could not apply that change.')` — plus a **Reload** link only when `isConflict(mutationError)`, under an explicit code comment: *"Reload, never retry: retrying replays the same stale intent over whoever wrote in between."* `WallDetailPage:141-147` renders `backendError` text **only** — no control at all (it refetches on its own). | `RetryBanner` cannot host these without either a `Retry` that the code forbids or a relabelled "RetryBanner" that says Reload. The "shared fault notice" the issue names but does not define is needed (§4). |
| `RetryBanner` "already cites `--color-accent-fault-subtle`/`-border`". | Confirmed: `RetryBanner.tsx:13`. **And the re-point has landed on `develop`**: `--color-accent-fault-subtle: var(--red-900)` (`tokens.css:148`, dark/`:root`), `var(--red-50)` (`:295`, light); `--color-accent-fault-border: color-mix(in oklch, var(--color-accent-fault) 40%, transparent)` (`:149`). | After spec 297, `RetryBanner` paints an opaque red tint stop; the six sites paint a 10 % alpha blend of `--red-500` over whatever ground is beneath. Visibly two different fault boxes, as the issue says. |
| (not stated) | **These six are the only triad call-site alpha matches outside `apps/kiosk-web`.** `grep -E '(bg\|border\|text\|ring\|outline\|fill\|stroke)-accent-(active\|warning\|fault)(-[a-z]+)?/[0-9]+'` over `apps/management-web/src` and `apps/shared/src` (non-test) returns exactly the 11 matches in these five files. `apps/shared/src/ui` is already guarded (`SharedUiTokenUsageTests`), `apps/kiosk-web/src` is guarded by spec 297's `WallStatusChipTests`; **`apps/management-web/src` has no guard.** | After this spec the console has zero matches, so a guard can land with **no allowlist** (US3). Without one, the seventh copy is a matter of reviewer memory. |
| (not stated) | **`RetryBanner` has no test of its own.** It is exercised only through page tests (`LayoutsPage.test.tsx:194`, `OverlaysPage.test.tsx:181`, `SystemVariablesPage.test.tsx:220`, `WallsPage.test.tsx`) that click "Retry". Nothing asserts its box. | Because this spec rewrites `RetryBanner` to draw through the new notice (§4), it gets a characterisation test written fresh and observed green first (§6). |
| (not stated) | **Every one of the six sites already has behavioural coverage** of role, text and control: the four mutation notices and their Reload/no-Reload cases (`LayoutsPage.test.tsx:297-323`, `OverlaysPage.test.tsx:697-759`, `SystemVariablesPage.test.tsx:175-204,276-293`, `RulesPage.test.tsx:310-369`), the wall's `backendError` (`WallDetailPage.test.tsx:82-106`), the rules load failure (`RulesPage.test.tsx:275-284`). **None asserts paint** — no class, no token. | No page needs a fresh characterisation test; each needs one new **red** paint case. The existing cases are the characterisation set and must pass unmodified. |

## 2. User stories

Each story is independently shippable once the foundation (US0) is in: its own call sites, its
own tests, observable on a running console without the others.

### US0 (P1, foundational — blocks US1–US3): one box, drawn once

`FaultNotice` (new, `apps/shared/src/ui/composites/FaultNotice.tsx`) renders its children in the
fault box: `role="alert"`, `--color-accent-fault-border` border, `--color-accent-fault-subtle`
fill, `--color-accent-fault` text — the class string `RetryBanner` carries today, moved.
`RetryBanner` draws its box through `FaultNotice`; its rendered DOM is unchanged.

```gherkin
Scenario: the notice is an alert on the fault tokens (happy path)
  Given a FaultNotice with the text "Could not apply that change."
  Then a single role="alert" element contains that text
  And its classes are exactly the fault box's: border-accent-fault-border, bg-accent-fault-subtle,
    text-accent-fault, with RetryBanner's spacing (mb-4 rounded-md border px-3 py-2 text-sm)
  And no class carries a call-site alpha modifier ("/NN")

Scenario: a control passed as a child is rendered inside the alert (happy path)
  Given a FaultNotice containing the text "Stale." and a button "Reload"
  Then the "Reload" button is a descendant of the alert

Scenario: RetryBanner is unchanged (characterisation)
  Given a RetryBanner with message "Could not load layouts."
  Then the alert reads "Could not load layouts. Retry", carries the same classes as before,
    and clicking "Retry" calls onRetry once
```

*Auth / conflict / bad request: N/A — a presentational composite with no request semantics.*

### US1 (P1): a refused change reads on the same box as a failed load

The four mutation-refusal notices (`LayoutsPage`, `OverlaysPage`, `SystemVariablesPage`,
`RulesPage:151-167`) and `WallDetailPage`'s refusal (`:141-147`) render through `FaultNotice`.
Text, Reload rule and role are unchanged; the fill becomes the opaque fault tint `RetryBanner`
already shows.

**Why P1:** it is the issue — five pages whose fault box no longer matches the one beside it.

```gherkin
Scenario: a refused publish shows the fault notice on the shared tokens (happy path)
  Given the layouts list, and publishing a layout is refused with 409 LAYOUT_NAME_TAKEN
  Then the alert reads "Could not apply that change."
  And its classes include bg-accent-fault-subtle and border-accent-fault-border
  And none of its classes carries a call-site alpha modifier

Scenario: a stale refusal still offers Reload, never Retry (conflict — unchanged)
  Given archiving an overlay is refused with 409 OVERLAY_REVISION_STALE and no detail
  Then the alert shows the conflict fallback and a "Reload" button inside it
  And there is no "Retry" button in it
  And clicking "Reload" re-reads the list

Scenario: a non-conflict refusal offers nothing to click (bad request — unchanged)
  Given setting a system variable is refused with 400 and no detail
  Then the alert reads "Could not apply that change." and contains no button

Scenario: a forbidden change is reported in the same notice (auth)
  Given publishing a rule is refused with 403 and detail "Forbidden."
  Then the alert reads that detail, on the fault tokens, with no "Reload"

Scenario: the wall's refused scene switch is the same notice (happy path, no control)
  Given switching a wall's scene is refused with 409 WALL_STALE
  Then the alert shows the conflict fallback on the fault tokens and contains no button
  And the wall is re-read, and the switch is not retried (unchanged)
```

### US2 (P1): the rules list's failed load is the RetryBanner every other list uses

`RulesPage`'s load-failure box (`:169-175`) becomes
`<RetryBanner message="Could not load rules." onRetry={…} />` — the same component `Layouts`,
`Overlays`, `SystemVariables`, `Walls`, `Cameras` and `Audit` already use for the same failure.

**Why P1:** the only one of the six that genuinely offers a retry, and the only one that
painted a *different* box (border only, default text colour, a `secondary` Button).

```gherkin
Scenario: a failed rules load offers Retry on the fault notice (happy path)
  Given the rules list query fails
  Then the alert reads "Could not load rules. Retry"
  And its classes include bg-accent-fault-subtle, border-accent-fault-border and text-accent-fault
  And clicking "Retry" re-fetches the list (unchanged)

Scenario: the rules table is not rendered while the load has failed (unchanged)
  Given the rules list query fails
  Then no "Automation rules" table is shown
```

*Conflict / bad request / auth: N/A — a read failure; any status renders the same sentence, as
it does on every other list page.*

### US3 (P2): the console cannot grow a seventh copy

A guard fails when any non-test `.ts`/`.tsx` under `apps/management-web/src` puts a call-site
alpha modifier on a triad colour in a string literal. No allowlist: after US1–US2 there is
nothing left to allow.

**Why P2:** not user-visible; it makes the outcome of US1–US2 permanent. Separable — if a
reviewer rules a console guard out of this issue's scope, drop US3 and nothing else moves.

```gherkin
Scenario: the console has no translucent triad colour (happy path)
  Given apps/management-web/src after US1 and US2
  Then the guard passes

Scenario: bad request — a page hand-rolls the fault box again
  Given a management-web page with className "border-accent-fault/40 bg-accent-fault/10"
  Then the guard fails naming that file and both matches
```

## 3. Scope

### 3.1 In this spec

- **US0.** `apps/shared/src/ui/composites/FaultNotice.tsx` (new) + its `exports` entry in
  `apps/shared/package.json`; `RetryBanner.tsx` draws through it.
- **US1.** `LayoutsPage.tsx:121-136`, `OverlaysPage.tsx:113-128`,
  `SystemVariablesPage.tsx:106-119`, `RulesPage.tsx:151-167`, `WallDetailPage.tsx:141-147`.
- **US2.** `RulesPage.tsx:169-175`.
- **US3.** `tests/Architecture.Tests/ConsoleTriadAlphaTests.cs` (new).

### 3.2 Not in this spec

| Item | Why not | Where it goes |
|---|---|---|
| The four mutation notices' shared **logic** — `problemDetail` + `isStaleConflict ? CONFLICT_FALLBACK : …` + `isConflict && Reload` is near-identical in four pages | The issue is about the box, not the sentence. Folding the logic into a `MutationErrorNotice` is a separate behaviour-preserving refactor with its own characterisation set; mixing it in would make every page diff larger than its paint change. | Observation only; not filed. A later issue may pick it up. |
| `RetryBanner` gaining an optional `onRetry` / an action label | Rejected in §4: five of six sites are not retries. | — |
| A `className`/`variant` prop on `FaultNotice` | No consumer needs one: every site already uses `mb-4` (or sits in a block `space-y-4` where `mb-4` is equivalent, §4.2). ADR-0036. | — |
| `ChainRecoveryNotice`'s editor-dialog alerts (`text-accent-fault`, no box) | Inline dialog text, not a page-level box; spec 156's focus/announcement contract lives there. Not a look-alike. | — |
| The wall's action buttons on `bg-accent-active/20` | Kiosk, affordance not status. | #2694. |
| Light-theme contrast of triad text; `color-mix(in oklch)` hue drift | Needs an ADR decision about ADR-0146 item 4 / ADR-0148 decision 2. This spec changes no token. | #2695. |

## 4. The component decision: a `FaultNotice`, with `RetryBanner` built on it

**Why not flex `RetryBanner`.** The natural extension — `onRetry` optional — covers exactly
one site (`WallDetailPage`). The other four non-retry sites need a **Reload** control, which
their own code distinguishes from retry on purpose (*"Reload, never retry"*). Covering them would
need an action-label prop, at which point `RetryBanner` is a generic fault box with a misleading
name and its file comment (*"there is no `variant` prop"*) is false. That is the
responsibility strain the brief warned of.

**Why this is not a second near-identical component.** The fault box's class string moves into
`FaultNotice` and lives **only** there; `RetryBanner` becomes `FaultNotice` + its Retry button.
One box, one class string, two names for two jobs: *something failed* (`FaultNotice`, children
supply the sentence and any control) and *a load failed, try again* (`RetryBanner`, unchanged
API, seven existing consumers untouched).

### 4.1 Contract

```ts
export interface FaultNoticeProps { children: ReactNode }
```

Renders `<div role="alert" className="mb-4 rounded-md border border-accent-fault-border
bg-accent-fault-subtle px-3 py-2 text-sm text-accent-fault">{children}</div>` — byte-for-byte
the element `RetryBanner` renders today. No other prop.

### 4.2 Spacing at `RulesPage`

`RulesPage`'s section is `space-y-4` and its two boxes carry no `mb-4`. Tailwind 4.3.3 emits
`space-y-4` as zero-specificity `:where(.space-y-4 > :not(:last-child))` margins, so the
component's `mb-4` wins on `margin-bottom` with the **same 1rem**. Expected visual change:
none. Confirmed by looking in phase 5 (§7 step 4), not assumed.

## 5. Why no new ADR

The tokens exist (issue #2523, spec 297), the composite tier exists (`RetryBanner`, `Badge`), and
ADR-0148 already says components cite semantic roles. Moving five call sites onto an existing
role through a composite applies those decisions. The US3 guard extends an existing rule (spec
257 US3's "no call-site alpha on a semantic colour", already enforced for `apps/shared/src/ui`
and, by spec 297, for `apps/kiosk-web/src`) to the one app that lacked it; it widens enforcement,
it does not narrow or reinterpret any rule.

## 6. Latency budget (§IV): **N/A**

Every file touched is reached only by `apps/management-web` (`RetryBanner` and `FaultNotice`
have no kiosk-web importer — `grep -rn RetryBanner apps/kiosk-web` is empty). No leg of the
event → overlay path is touched. No token value changes.

## 7. Phase-4a colour: **red** per call site, **characterisation** for `RetryBanner`

Following spec 297 §6: a site whose rendered paint changes is behaviour-changing, even when it
is "the same kind of thing visually". Every page site's fill changes (alpha blend of
`--red-500` → opaque `--red-900` stop; `RulesPage:170` additionally gains a fill, fault text and
a link-style Retry). `RetryBanner`'s DOM does not change, so it is characterised.

| Site | Colour | New red test (fails on `56f2cde8`) | Characterisation (green before, unmodified after) | Fresh characterisation needed? |
|---|---|---|---|---|
| `FaultNotice` (US0) | red | `FaultNotice.test.tsx` against a signature-only stub | — | — |
| `RetryBanner` (US0) | **characterisation** | — | new `RetryBanner.test.tsx`: role, text, classes, Retry → `onRetry` | **Yes — no test of its own exists** |
| `LayoutsPage` (US1) | red | refused change → alert on fault tokens, no `/NN` | `LayoutsPage.test.tsx:194` (Retry), `:285-325` (stale fallback, Reload) | No |
| `OverlaysPage` (US1) | red | same | `OverlaysPage.test.tsx:181`, `:697-760` | No |
| `SystemVariablesPage` (US1) | red | same | `SystemVariablesPage.test.tsx:175-204`, `:220`, `:263-295` | No |
| `RulesPage` mutation (US1) | red | same | `RulesPage.test.tsx:300-370` | No |
| `WallDetailPage` (US1) | red | same, and the alert contains no button | `WallDetailPage.test.tsx:73-107` (+ the ADR-0151 cases after it) | No |
| `RulesPage` load failure (US2) | red | failed load → alert on fault tokens | `RulesPage.test.tsx:275-284` (`Offers a retry when the list fails to load`) | No |
| Console guard (US3) | red | `ConsoleTriadAlphaTests` names exactly the 11 matches in 5 files (§1) | — | — |

**No existing test line may change.** Any existing case that must be edited to pass is evidence
the behaviour moved: stop and report. `e2e/{layouts,overlays,rules,system-variables}.spec.ts`
address these alerts by role and text only and stay green unmodified in CI.

Any case declared red here that arrives green is a stop-and-report.

## 8. Independent end-to-end test procedure

Against the running Aspire stack (`dotnet run --project src/AppHost`), management-web in
Chromium, dark theme:

1. **US1, conflict.** Open Layouts in two tabs. Publish a layout in tab A; in tab B publish the
   same layout → the notice shows the conflict fallback and *Reload* on the **red tint**,
   matching a `RetryBanner` (provoke one by stopping the layouts API briefly and reloading
   another list page). Click Reload → list re-reads. Repeat for Overlays (archive), Rules
   (publish), System variables (set value).
2. **US1, wall.** Two tabs on one wall's detail; switch scene in A, then in B → conflict
   fallback on the red tint, no button.
3. **US2.** Stop the automation API (or block `**/rules**` in DevTools) and open Rules → *Could
   not load rules. Retry* on the red tint; restore and click Retry → the table renders.
4. **Spacing.** On Rules, compare the gap between the filter group and the notice before and
   after (screenshot pair) — §4.2 expects identical.
5. Screenshots of 1–4. Read one notice's computed `background-color` in DevTools: it equals
   `RetryBanner`'s, and is opaque.

## 9. Functional requirements

- **FR-001** `FaultNotice` renders its children inside one `role="alert"` element whose class
  string is exactly §4.1's. It has no prop besides `children`.
- **FR-002** `RetryBanner`'s public props (`message`, `onRetry`) and rendered DOM are unchanged;
  it renders through `FaultNotice`, and the fault class string appears in `FaultNotice.tsx` only.
- **FR-003** Each US1 site renders its refusal through `FaultNotice`, keeping its text, its
  Reload-iff-`isConflict` rule (four pages) or no control (`WallDetailPage`), and its position.
- **FR-004** `RulesPage`'s load failure renders `RetryBanner` with message
  `"Could not load rules."` and `onRetry` re-fetching the list.
- **FR-005** No `.ts`/`.tsx` file under `apps/management-web/src` (tests excluded) contains
  `(bg|border|text|ring|outline|fill|stroke)-accent-(active|warning|fault)/<n>` in a string
  literal; an architecture test enforces it with no allowlist.

## 10. Success criteria

- **SC-1** Every FR has a test; every red one observed red first and quoted verbatim in the PR.
- **SC-2** The §7 characterisation set is green before any source edit and green, unmodified,
  after.
- **SC-3** `grep -rnE 'accent-(active|warning|fault)/[0-9]' apps/management-web/src --include=*.tsx`
  returns nothing outside `*.test.*`.
- **SC-4** `pnpm lint`, `pnpm typecheck`, `pnpm format:check`, `pnpm test`;
  `dotnet test tests/Architecture.Tests`; e2e green in CI.
- **SC-5** §8 screenshots show each notice on the same fill as `RetryBanner`.
