# Spec 145 — Plan

**Phase:** 2 (Plan) · **Date:** 2026-09-13 · **Tree read at:** `3fe61674`
**Spec:** `specs/145-a-retried-pass-says-so/spec.md`
**Implementer:** `infra-engineer` (CI workflow + Playwright config + a
repository script; no application code in any bounded context).

---

## Bounded context and layers — none, and that is the point

**This change touches no bounded context and no layer of the DDD model.** It
lives entirely in repository tooling:

| File | Role |
|---|---|
| `playwright.config.ts` | test-runner configuration (repo root, outside the pnpm workspace per ADR-0108) |
| `.github/workflows/ci.yml` | CI definition |
| `scripts/summarise-e2e-retries.mjs` | new — the summariser |
| `scripts/summarise-e2e-retries.test.mjs` | new — its tests, run by `pnpm test:guards` |

Consequently the usual plan headings collapse, and each is answered rather than
skipped:

- **Entities / value objects:** none. The only data is Playwright's own
  `JSONReport` shape, which is a wire format owned by a third party, not a
  domain model. Constitution §II binds domain models; a JSON report read in a
  Node script is the same category as `Shared.Contracts` — primitives are the
  native vocabulary.
- **Domain → integration event:** none. Nothing is published, nothing is
  consumed, no RabbitMQ queue, no outbox.
- **Persistence:** none. No Postgres, no migration, no Marten.
- **HTTP surface:** none. No endpoint, no DTO, no scope, no `Idempotency-Key`.

The `Ensure.That`, `Option<T>`, `Result<T, Error>`, deconstruction and
collection-expression rules in CLAUDE.md are C# rules and do not reach a `.mjs`
file. The applicable conventions are the ones the three existing
`scripts/*.test.mjs` guards establish, and the implementer should read
`scripts/lint-scope.test.mjs` and `scripts/wait-for-e2e-stack.test.mjs` before
writing a line — house style there is a commented header naming the defect the
guard exists for, and `node:test` + `node:assert/strict`.

## The mechanism, and the alternative that was rejected

**Chosen: `json` reporter → file → a separate Node script → `$GITHUB_STEP_SUMMARY`.**

```
pnpm test:e2e
  └─ reporter: [list, html, json(outputFile: test-results/e2e-report.json)]
       └─ (CI step, if: always())
            node scripts/summarise-e2e-retries.mjs [path]
              └─ writes Markdown to $GITHUB_STEP_SUMMARY (append), or stdout
```

**Rejected: a custom Playwright reporter class** implementing `onTestEnd` /
`onEnd` and writing the summary from inside the test process. It is fewer
moving parts and it survives a job cancellation better, but:

- it is code running inside the runner's test process, so testing it means
  booting Playwright — against a pure function over a JSON file, which
  `node --test` can exercise in milliseconds against six fixtures;
- it would be the first `.ts` file in the repository that is neither app,
  e2e spec nor config, and would sit outside both the `apps/**` and `e2e`
  tooling scopes (A3);
- the split version reuses a pattern the repository already has three
  instances of, which CLAUDE.md's "read before write / mirror existing
  patterns" rule prefers outright.

The cost of the rejection is the missing-file case, which is exactly why
**FR-006 exists and is a tested scenario** rather than an afterthought.

## Data shape — verified against the installed version, not remembered

`@playwright/test@1.62.1` → `playwright/types/testReporter.d.ts`:

- `JSONReport` (`:267`) — `{ config, suites: JSONReportSuite[], errors, stats }`
- `stats` (`:283`) — `{ startTime, duration, expected, unexpected, flaky, skipped }`
- `JSONReportSuite` (`:294`) — `{ title, file, specs, suites?: JSONReportSuite[] }`
  → **nested; the walk must recurse**, and a fixture must exercise that.
- `JSONReportSpec` (`:303`) — `{ title, ok, tests: JSONReportTest[], file, line }`
- `JSONReportTest` (`:314`) — `{ projectName, results: JSONReportTestResult[],
  status: 'skipped' | 'expected' | 'unexpected' | 'flaky' }`
  → **`status === 'flaky'` is the signal.** Playwright's own definition
  (`testReporter.d.ts:469`): "Test that passes on a second retry is `'flaky'`."
  Nothing needs to be inferred from attempt counts.
- `JSONReportTestResult` (`:329`) — `{ retry, duration, status: TestStatus |
  undefined, error, errors, stdout, stderr, attachments, … }`
  → `retry` is the attempt index; `status` **may be `undefined`** (FR-004).
  → `stdout` / `stderr` / `error` / `attachments` are **never rendered**
  (spec, *Auth*). Read only `status` and `retry`.

One shape note for the implementer: a `spec` carries one `test` entry **per
Playwright project**, and this config has five projects (`chromium`, `seed`,
`kiosk`, `wall`, `cleanup`). Report the project name per row, and count tests,
not specs — that is what `stats.flaky` counts too, which is what makes FR-005's
cross-check meaningful.

## Where the report file goes, and why there

**`test-results/e2e-report.json`.** Both properties are already true of that
directory and neither needs a new line anywhere:

- gitignored (`.gitignore:70` — `/test-results/`), so a local run cannot dirty
  the tree;
- inside the existing artifact upload (`ci.yml:283` lists `test-results`), so
  the raw report is downloadable beside the HTML one without touching the
  upload step.

The risk is A1 — Playwright cleans `outputDir` at the start of a run, and the
reporter writes at the end. Believed safe; **phase 5 must confirm the file is
in the downloaded artifact**, and the spec records the fallback (repo-root path
+ one `.gitignore` line + one upload entry) so a failure here is a two-line
change, not a redesign.

The path is passed to the script as `process.argv[2]` with the same value as a
default, so the test can point it at a fixture and CI need not repeat it.

## Rendered shape

One `<h3>`-level section, appended to the step summary. Sketch, not a
specification of exact bytes — the tests assert the load-bearing substrings
named in the acceptance scenarios, not the whole document:

```markdown
### Playwright e2e — retried outcomes

**3 tests passed only on retry.** They are green in the gate. Locally
`retries: 0`, so the same tests would be red on a developer machine.

| Outcome | Tests |
| --- | --- |
| Passed first attempt | 41 |
| Passed only on retry | 3 |
| Failed | 0 |
| Skipped | 2 |

| Test | Project | Attempts | Statuses |
| --- | --- | --- | --- |
| `e2e/cameras.spec.ts` › registers a camera | chromium | 2 | timedOut → passed |
```

Two rules the implementer must not soften:

1. **The section is emitted in every case** (FR-003) — zero flaky, a red run,
   a missing report. Its absence from a job page is then itself a defect
   report.
2. **The zero case and the missing case read differently** (FR-006). "0 tests
   passed only on retry" and "no report was found" are the two states a naive
   implementation collapses, and collapsing them is precisely the failure mode
   this spec exists to prevent one level up.

## Invariants the change must preserve

- `playwright.config.ts:13` and `:15` are **unchanged**, character for
  character. Asserted, not promised (FR-008 / T004).
- The `list` and `html` reporter entries are unchanged; `json` is **appended**
  to the CI array. The non-CI array stays `[['list']]`.
- `pnpm test:e2e` exits with the code it exits with today. The summary step is
  a separate step, `if: always()`, and cannot change the job result — a script
  that throws would, so it must not throw (FR-007).
- No new entry in `package.json`'s `dependencies` or `devDependencies`.
- No change to `pnpm lint`, `pnpm format:check` or their scopes (A3).

## Boundary rules

No cross-context project reference is possible — no C# project is touched, so
`NetArchTest` is unaffected and the `.slnx` is unchanged. The one boundary that
*is* live here is the spec's *Auth* note: the summariser must not copy test
process output into a publicly readable job summary. Keep the field access
list to `status`, `retry`, `projectName`, `title`, `file`.

## File ownership and collisions (ADR-0109)

Four files, three of them new or near-new, all disjoint from any application
code:

| File | New? | Contended? |
|---|---|---|
| `scripts/summarise-e2e-retries.mjs` | yes | no |
| `scripts/summarise-e2e-retries.test.mjs` | yes | no |
| `playwright.config.ts` | no — one array literal at `:17` | **yes, mildly** |
| `.github/workflows/ci.yml` | no — one inserted step after `:258` | **yes, mildly** |

`playwright.config.ts` and `ci.yml` are the two files every in-flight CI or e2e
branch touches, and rebase-merge (ADR-0087) means a parked branch replays old
copies of whatever landed. **Keep both edits to the minimum number of lines**
so a conflict, if one comes, is resolvable by inspection. The two `scripts/`
files are `[P]`-safe against anything.

## Risks

1. **A1 — the JSON file does not survive `outputDir` cleanup.** Detected at
   phase 5 by the artifact download, not by a green test. Fallback pre-decided
   (spec, A1). *Likelihood: low. Cost if hit: two lines.*
2. **A green run shows zero, and zero proves nothing about counting.** This is
   the real risk of shipping a reporting mechanism: it looks finished when it
   is merely silent. Mitigated by the counterfactual in the spec's e2e
   procedure (step 4) and by six fixtures, which is why phase 4a is not
   optional here. MEMORY: "guards that read the design artefact prove the
   design was written down, not that it holds".
3. **`stats.flaky` and the tree disagree after a Playwright upgrade.** FR-005
   turns this from a silent zero into a printed contradiction.
4. **A very large report.** `$GITHUB_STEP_SUMMARY` is capped at 1 MiB per step
   by GitHub, and a truncated summary is a silently wrong one. The suite is 23
   spec files across 5 projects, so the flaky table cannot plausibly approach
   that — but the script must **not** render error text or stdout (which
   could), which the *Auth* constraint already forbids for a different reason.
   Two reasons, one rule.
5. **The step runs when `pnpm test:e2e` never ran at all** (a failure in an
   earlier step of the job, e.g. the stack never booting). `if: always()` means
   the step fires; FR-006 means it says "no report found", which is the correct
   and informative reading of that job. Not a bug — but it must be a tested
   scenario, which it is.

## Gate — phase 2

The plan aligns with the constitution and ADRs. It introduces no architecture,
no dependency, no context, no persistence and no event; it operates ADR-0108
and mirrors an existing three-instance pattern rather than inventing one; it
leaves every gate value in `playwright.config.ts` untouched, and says how that
is asserted rather than asking to be believed.
