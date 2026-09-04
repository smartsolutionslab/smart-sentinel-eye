# Implementation Plan: The first write of a run

**Feature**: `066-the-first-write-of-a-run` · #2014 · branch
`fix/2014-the-first-write-of-a-run`

**Spec**: `specs/066-the-first-write-of-a-run/spec.md`

---

## The three declarations (ADR-0144)

### Declaration 1 — which engineer

**`frontend-engineer`.**

Every file changed is a Playwright spec or support module under `e2e/`, plus one
new module in `e2e/support/`. No `src/**` file is touched, no CI workflow, no
Aspire resource, no shell script. The frontend brief owns e2e coverage, and the
frontend reviewer already reviews `e2e/`.

**Not `infra-engineer`**, and that is a consequence of the ruling: the infra lane
would have owned this had the answer been (b), because
`scripts/wait-for-e2e-stack.sh` and `ci.yml` are its files. (b) is rejected, so
neither is opened.

### Declaration 2 — is the honest answer a new ADR?

**No.** Nothing here decides architecture; it applies decisions already made.

- **ADR-0108** already fixes the arrangement this feature lives inside: Playwright
  at the repo root, against a live `aspire run` stack, with no Playwright-managed
  `webServer`. A per-assertion timeout inside that arrangement is a test-suite
  convention, not a new decision about it.
- **The one thing that would have needed an ADR is the thing being rejected.**
  Adding an authenticated, data-creating readiness gate to the script every CI
  job depends on changes what "the stack is ready" means for the whole
  programme — that is ADR-shaped, and this lane may not write one. It is
  therefore not proposed, and the rejection rests on spec 023's measurements
  rather than on the constraint.
- **Spec 023 set the precedent for recording this kind of finding without an
  ADR**: it refuted eight candidate mechanisms and recorded the lot in
  `verification.md`. This feature's finding — that a write-readiness probe cannot
  fix a per-message-type cost — belongs in the same place.

**Not blocked.**

### Declaration 3 — behaviour-changing or behaviour-preserving

**Behaviour-changing → phase 4a is RED.** Ambiguity resolves that way by rule,
and here it is not even ambiguous: an assertion that used to time out at 15 s
must stop timing out. Something observable changes.

**How the red is produced and captured — and it does not need a cold stack.**

The brief asked for a red that can be produced on demand, and one is available.
A cold stack is *not* it:

- A cold stack is single-use. Producing the red means tearing down and rebooting
  the whole stack, and it cannot be re-run to confirm.
- Spec 023 §5 established that **restarting one service does not reproduce the
  cost** — the cheap-looking shortcut (restart `system-variables`, run the file)
  is known not to work. Recorded so the test-writer does not spend a boot
  discovering it.
- It is machine-dependent. A faster host may serve the first write inside 15 s.

**The red relied upon is the injected-delay test** (Tier 1 in spec.md): a
Playwright route interception holds the define `POST` for 20 s, so the assertion
after it is red at the 15 s default and green at the 90 s budget, deterministically,
on a warm stack, as many times as anyone wants.

The cold-stack run is Tier 2: corroboration, recorded in the verification note
whether it reproduces or not, and **not the gate**.

---

## Phase 4a design — the red test, exactly

New test in `e2e/system-variables.spec.ts` (the file under repair — a red test
that lives beside what it is about).

```ts
// Written by the test-writer at 4a with NO explicit assertion timeout — the
// same shape every exposed site uses today. That is what makes it red.
test('a define the service is slow to answer still appears in the list', async ({ page }) => {
  await signInAsOperator(page);

  // Only the POST. The list GET is the SAME url (`url: ''` in
  // systemVariables.api.ts), so a route that does not discriminate by method
  // would delay the read as well and the test would be red for the wrong
  // reason. A URL predicate rather than a glob, because the GET carries query
  // parameters and the POST may carry `?fabId=`.
  await page.route(
    (url) => url.pathname.endsWith('/system-variables/system-variables'),
    async (route) => {
      if (route.request().method() !== 'POST') {
        return route.fallback();
      }
      await new Promise((resolve) => setTimeout(resolve, SLOW_WRITE_DELAY_MS));
      await route.continue();
    },
  );

  await page.getByRole('link', { name: /^system variables$/i }).click();
  await expect(page.getByRole('heading', { name: 'System variables', exact: true })).toBeVisible();

  await page.getByRole('button', { name: /new variable/i }).click();
  const name = `E2E_Slow_${Date.now()}`;
  await page.locator('#variable-name').fill(name);
  await page.getByRole('button', { name: /^define$/i }).click();

  await expect(page.getByText(name)).toBeVisible();   // ← 4b changes ONLY this line
});
```

`SLOW_WRITE_DELAY_MS = 20_000`, declared in the same file at 4a.

**Why 20 s and not 40 s.** It must exceed the local default (15 s) so it is red
locally, and stay under CI's `expect.timeout` (30 s) so **the same test proves
the same thing in CI**. A 40 s delay would be red in both and would also blow the
60 s per-test timeout, making the failure a test timeout rather than an assertion
timeout — a different, less legible red.

**What the engineer may and may not change at 4b — checkable, not a matter of
judgement:**

| | |
|---|---|
| **May change** | the assertion's timeout, at the marked line only |
| **May not change** | `SLOW_WRITE_DELAY_MS`, the route interception, the method guard, the asserted locator, or the test's name |

This matters because the code under repair *is* test code, so "the engineer may
not edit the tests to pass" needs a sharper edge than usual. The edge is: the
timeout is the change; everything else is the test.

**The expected red, in shape** (the verbatim text is the phase-4a artifact):

```
  1) [chromium] › system-variables.spec.ts:NN:N › a define the service is slow to answer still appears in the list

    Error: Timed out 15000ms waiting for expect(locator).toBeVisible()

    Locator: getByText('E2E_Slow_1757...')
    Expected: visible
    Received: <element(s) not found>
```

---

## Phase 4b design — the shape the change lands in

### The shared budget — one module, two numbers, the reasoning once

`e2e/support/cold-stack.ts` (new):

```ts
/**
 * The budget for an assertion that follows the first write of its kind in a run.
 *
 * A freshly booted stack pays a near-constant cost the first time a given
 * integration message type is published — about 5 s, measured and confirmed by
 * intervention in specs/023-first-event-cold-start/verification.md §3, and
 * **still unexplained**: eight candidate mechanisms were refuted there, and none
 * is standing. The cost attaches to the message type, not to the process, so a
 * file being second in the run does not make it safe.
 *
 * 90 s is not a new number: it is what the two spec-056 seeds already carry,
 * and roughly six times the worst cold journey on record (14 s).
 *
 * Use it for the FIRST assertion after each distinct kind of write in a file.
 * Not for repeat writes of the same kind (measured warm at 134-270 ms), and
 * never for an assertion on an error surface — widening a wait for something
 * that should never appear turns every failure into a stall.
 */
export const FIRST_WRITE_TIMEOUT_MS = 90_000;

/**
 * The per-test timeout a test needs in order for FIRST_WRITE_TIMEOUT_MS to mean
 * anything. playwright.config.ts sets 60 s, so a 90 s assertion budget inside a
 * default test is capped at 60 s minus everything already elapsed — the number
 * in the source would be decoration. The two seeds escape this only because
 * they also call setTimeout(180_000); none of the nine exposed spec files does.
 */
export const FIRST_WRITE_TEST_TIMEOUT_MS = 180_000;
```

### The call sites

```ts
test('operator defines a system variable and it appears in the list', async ({ page }) => {
  test.setTimeout(FIRST_WRITE_TEST_TIMEOUT_MS);
  ...
  await expect(page.getByText(name)).toBeVisible({ timeout: FIRST_WRITE_TIMEOUT_MS });
});
```

And the two seeds lose their duplicated literal and six-line comment:

```ts
await expect(page.getByRole('heading', { name: wall.variableName })).toBeVisible({
  timeout: FIRST_WRITE_TIMEOUT_MS,
});
```

Their `setTimeout(180_000)` calls become `setTimeout(FIRST_WRITE_TEST_TIMEOUT_MS)`
— same value, one name.

### What deliberately keeps the default budget

- `await expect(page.getByRole('alert')).toHaveCount(0)` — an assertion that
  something is *absent*.
- The second and third define in `system-variables.spec.ts` **do** still take the
  budget: they are separate tests, and Playwright gives no ordering guarantee
  strong enough to call any of them "the warm one". Within a *single* test, a
  repeat write of the same kind keeps the default.
- Navigation and list-load assertions (`heading 'System variables'`) — those are
  reads, and reads are not the class.

---

## Architecture

### Bounded context and layers

**None — and that is the point.** This feature changes no bounded context, no
Domain, Application, Infrastructure or Api code, and adds no contract. It changes
the test harness's model of how long a cold write may take.

The services whose behaviour is being *accommodated* are five —
camera-catalog, overlay-designer, layout-composition, automation,
system-variables — but none of them is edited.

### Entities, value objects, invariants

None. Constitution §II does not bite: nothing here is a domain model, and
`e2e/**` is TypeScript.

### Messaging

None added. The messaging that *causes* the problem is already in place:
`OutboxEventBus<TDbContext>` captures an integration event into the Wolverine
outbox bound to the caller's `DbContext`, released on commit (ADR-0088, spec 021).
For system-variables that is `SystemVariableDefinedV1` on define and
`SystemVariableValueChangedV1` on set-value — **two message types, so two
first-payments**, which is why one shared readiness write could not have covered
even this one file.

### Boundary rules

Unaffected. No cross-context project reference is created or removed; NetArchTest
has nothing to say about `e2e/`.

### Files touched

| File | Change | Story |
|---|---|---|
| `e2e/support/cold-stack.ts` | **new** — two constants + the reasoning | US1 |
| `e2e/system-variables.spec.ts` | the red test (4a); then the budget at 5 sites + `test.setTimeout` | US1 |
| `e2e/support/seed-live-video-wall.setup.ts` | literal → constant, comment → import | US1 |
| `e2e/support/seed-bound-overlay-wall.setup.ts` | literal → constant, comment → import | US1 |
| `e2e/cameras.spec.ts` | 1 site | US2 |
| `e2e/camera-detail.spec.ts` | 4 sites | US2 |
| `e2e/overlays.spec.ts` | 1 site | US2 |
| `e2e/layouts.spec.ts` | 5 sites | US2 |
| `e2e/rules.spec.ts` | 2 sites | US2 |
| `e2e/support/seed-published-layout.setup.ts` | 3 sites | US2 |
| `e2e/kiosk-reconciliation.spec.ts` | 1 site | US2 |
| `e2e/kiosk-shows-a-label-over-video.spec.ts` | 1 site | US2 |

**Not touched, deliberately**: `scripts/wait-for-e2e-stack.sh`,
`.github/workflows/ci.yml`, `playwright.config.ts`, anything under `src/`.

---

## Constitution and ADR alignment

| Rule | How this complies |
|---|---|
| **ADR-0108** — Playwright e2e against a live Aspire stack, no `webServer` | unchanged; the fix lives entirely inside that arrangement |
| **ADR-0088** — Wolverine outbox, per-module queues, eager transactions | not modified; named as the write path whose cold cost is being accommodated |
| **ADR-0067** — `MigrationRunner` | named and excluded: spec 023 §4 ruled out startup storage build as the mechanism |
| **ADR-0113** — `If-Match` on set-value | preserved; FR-005 keeps refusals fast so a 428/409 is still caught |
| **ADR-0036** — smallest change, no speculative generality | two constants, no env var, no config knob, no new abstraction; the sweep removes two duplicated literals rather than adding a framework |
| **ADR-0144** — the lane may not weaken a gate | `retries` and `expect.timeout` untouched; the asymmetry is raised as a recommendation (US3) |
| **ADR-0144** — the lane may not write an ADR | none needed; the one ADR-shaped option is the one being rejected |
| **ADR-0139 / constitution §Testing** — new behaviour starts red | the injected-delay test, red on demand, verbatim output quoted in the PR |
| **ADR-0030 / ADR-0086** — Conventional Commits, no `Co-Authored-By` | see tasks.md |

### Latency budget impact (constitution §IV)

**N/A — no leg.** No production code path changes.

Stated with care because the subject is latency and a careless reading could take
this feature for a §IV discharge. It is not one. Spec 023's ~5 s-per-message-type
cold cost remains an open, unexplained risk to the 200 ms *event → overlay state*
leg on a full cluster start; this feature stops the e2e suite reporting that risk
as a product failure, and does nothing to the risk itself.

---

## Risks

| Risk | Mitigation |
|---|---|
| **90 s is not enough on some machine.** | It is 6× the worst figure on record. If exceeded, the budget is the wrong answer and the mechanism is the right question — reopen spec 023, do not raise the number. |
| **A genuine failure now takes 90 s to report.** | Bounded by FR-005/FR-006: error assertions and repeat writes keep the default. Worst case is one slow failure per file, not per assertion. |
| **The sweep silences a real regression.** | The precise risk of a blanket. Countered by scoping to first-of-kind sites and by leaving every alert assertion at the default: a broken write still fails, it just fails through the alert path. |
| **`route.fallback()` misuse delays the reads too.** | Called out in the 4a design with the reason. A test that delays the GET would be red before *and* after the fix and would look like a failed fix. |
| **The Tier 2 cold run does not reproduce.** | Explicitly not the gate. Recorded either way. Do **not** substitute a green characterisation test — the honest record of a non-reproduction is worth more than a test that proves nothing. |
| **`test.setTimeout` forgotten at a site.** | The most likely way this fix looks applied and is not. `FIRST_WRITE_TEST_TIMEOUT_MS` exists so the pair travels together, and the review checks them as a pair. |

---

## What is explicitly not being built

- A write-completing readiness probe in `scripts/wait-for-e2e-stack.sh`.
- Any change to CI retries or the CI/local timeout asymmetry.
- A source-scanning guard that asserts the convention is applied.
- An explanation of the ~5 s per-message-type cost.
- A way to delete accumulated E2E system variables.
