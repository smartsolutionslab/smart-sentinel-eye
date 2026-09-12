# Verification 136 — The figure is from CI

**Phase 5 (ADR-0037).** Issue #2148. Branch
`docs/2148-a-figure-read-where-the-budget-does-not-apply`.

This note is the phase-5 artefact for a change whose entire diff is prose and
comments. It records three things: the mechanical invariant that proves no
threshold moved, the measurement the prose now quotes, and — at the end, and at
the same length — **what it does not verify**.

## 1. The invariant: no executable line changed

The claim the change has to earn is that `NFR002_MqttConnectAuthTests.cs` gained
31 comment lines and nothing else. The check is an extract of the file's
non-comment, non-blank lines with leading indentation stripped, hashed:

```sh
grep -vE '^\s*(///|//)' tests/Integration.Tests/Identity/NFR002_MqttConnectAuthTests.cs \
  | grep -vE '^\s*$' \
  | sed -E 's/^[[:space:]]+//' \
  | sha256sum
```

| | `develop` (before) | branch tip (after) |
|---|---|---|
| extract | **124 lines** | **124 lines** |
| sha256 | `03d65114af3ba42b36856384691d03c4c1183e9b8f9c82ae48de2d710321213a` | `03d65114af3ba42b36856384691d03c4c1183e9b8f9c82ae48de2d710321213a` |
| file LOC | 211 | **242** (ADR-0084 cap: 300) |

Re-run it against `git show develop:<path>` to reproduce the "before" column.
`P50BudgetMilliseconds` is still `15` (`:51`), `P99CeilingMilliseconds` is still
`50` (`:55`), and `BudgetsApplyHere` (`:123-124`) is byte-identical.

**What this check does and does not catch.** It catches any edit to an
executable line, to a trailing `// …` on a line of code, and any added or
removed statement. It does **not** catch a pure re-indentation (leading
whitespace is stripped, deliberately, so the hash survives an editor's tab
settings) and it does not catch a change confined to a `/* … */` block — this
file has none. Re-indentation cannot change behaviour, so the residue is
acceptable; it is stated because an unqualified "byte-identical" would be the
same kind of overclaim this spec exists to correct.

## 2. The measurement: 40 green CI runs

Every green `ci.yml` run on `develop` between **2026-09-07 05:07Z** and
**2026-09-11 21:04Z** — forty of them, no run in the window skipped — with its
`integration-test-results` artifact downloaded and the test's own
`output.WriteLine` line read out of `integration.trx`. All forty end
`(budgets enforced)`; none breached either threshold.

| run | date (UTC) | p50 ms | p99 ms | max ms | p50 margin /15 | p99 margin /50 |
|---|---|---|---|---|---|---|
| 34647527830 | 2026-09-11 21:04 | 1.68 | 8.15 | 39.75 | 8.92× | 6.13× |
| 34631985515 | 2026-09-11 18:12 | 1.98 | 8.88 | 8.94 | 7.57× | 5.63× |
| 34607694151 | 2026-09-11 14:01 | 2.21 | 4.53 | 6.26 | 6.78× | 11.03× |
| 34589148935 | 2026-09-11 10:25 | 2.29 | 6.72 | 11.50 | 6.55× | 7.44× |
| 34586548968 | 2026-09-11 09:54 | 2.40 | 7.70 | 16.58 | 6.25× | 6.49× |
| 34573183563 | 2026-09-11 07:10 | 2.11 | 4.84 | 10.40 | 7.10× | 10.33× |
| 34535169347 | 2026-09-10 22:00 | 2.46 | 7.04 | 21.18 | 6.09× | 7.10× |
| 34529861630 | 2026-09-10 21:02 | 2.31 | 6.38 | 6.97 | 6.49× | 7.83× |
| 34519576651 | 2026-09-10 19:18 | 1.03 | 1.99 | 6.57 | 14.56× | 25.12× |
| 34511971761 | 2026-09-10 18:03 | 2.46 | 5.10 | 9.24 | 6.09× | 9.80× |
| 34497143153 | 2026-09-10 15:40 | 2.09 | 5.44 | 6.60 | 7.17× | 9.19× |
| 34475397402 | 2026-09-10 12:12 | 3.55 | 12.97 | 17.84 | 4.22× | 3.85× |
| 34467417391 | 2026-09-10 10:41 | 1.72 | 5.81 | 9.99 | 8.72× | 8.60× |
| 34460898330 | 2026-09-10 09:29 | 1.13 | 3.51 | 4.11 | 13.27× | 14.24× |
| 34445915797 | 2026-09-10 06:35 | 2.01 | 7.41 | 33.57 | 7.46× | 6.74× |
| 34439075596 | 2026-09-10 04:55 | 2.58 | 6.32 | 7.17 | 5.81× | 7.91× |
| 34405551406 | 2026-09-09 21:11 | 2.72 | 10.41 | 10.85 | 5.51× | 4.80× |
| 34394730440 | 2026-09-09 19:23 | 2.69 | 7.28 | 17.71 | 5.57× | 6.86× |
| 34390665298 | 2026-09-09 18:42 | 2.76 | 6.01 | 6.91 | 5.43× | 8.31× |
| 34370422034 | 2026-09-09 15:28 | 1.83 | 4.70 | 4.75 | 8.19× | 10.63× |
| 34347307456 | 2026-09-09 11:46 | 2.03 | 4.72 | 7.68 | 7.38× | 10.59× |
| 34327047150 | 2026-09-09 08:03 | 1.90 | 6.12 | 7.01 | 7.89× | 8.16× |
| 34292952397 | 2026-09-08 23:58 | 2.39 | 6.96 | 7.00 | 6.27× | 7.18× |
| 34282077466 | 2026-09-08 21:42 | 2.60 | 6.98 | 8.60 | 5.76× | 7.16× |
| 34276217698 | 2026-09-08 20:40 | 1.87 | 5.27 | 12.03 | 8.02× | 9.48× |
| 34270570358 | 2026-09-08 19:42 | 2.17 | 5.38 | 7.01 | 6.91× | 9.29× |
| 34257864427 | 2026-09-08 17:33 | 2.25 | 6.75 | 12.41 | 6.66× | 7.40× |
| 34238777829 | 2026-09-08 14:31 | 2.08 | 5.90 | 6.22 | 7.21× | 8.47× |
| 34227819749 | 2026-09-08 12:44 | 1.48 | 6.64 | 7.20 | 10.13× | 7.53× |
| 34207261636 | 2026-09-08 08:56 | 2.39 | 5.74 | 23.56 | 6.27× | 8.71× |
| 34198838321 | 2026-09-08 07:20 | 1.18 | 4.92 | 12.93 | 12.71× | 10.16× |
| 34160441452 | 2026-09-07 20:42 | 2.45 | 9.32 | 12.33 | 6.12× | 5.36× |
| 34148650344 | 2026-09-07 17:41 | 2.13 | 10.51 | 15.74 | 7.04× | 4.75× |
| 34144331243 | 2026-09-07 16:40 | 1.55 | 5.00 | 5.63 | 9.67× | 10.00× |
| 34135949905 | 2026-09-07 14:59 | 2.34 | 5.47 | 11.64 | 6.41× | 9.14× |
| 34125316528 | 2026-09-07 13:04 | 2.84 | 6.25 | 6.98 | 5.28× | 8.00× |
| 34117479948 | 2026-09-07 11:36 | 1.41 | 5.00 | 6.94 | 10.63× | 10.00× |
| 34090904598 | 2026-09-07 06:27 | 2.62 | 7.79 | 8.01 | 5.72× | 6.41× |
| 34086712880 | 2026-09-07 05:24 | 3.26 | 7.46 | 13.54 | 4.60× | 6.70× |
| 34085564002 | 2026-09-07 05:07 | 2.09 | 6.63 | 7.36 | 7.17× | 7.54× |

Margins are truncated, never rounded up, so no figure in this tree claims more
headroom than was measured.

| | min | max | mean | margin floor | margin ceiling |
|---|---|---|---|---|---|
| p50 vs 15 ms | 1.03 ms | 3.55 ms | 2.176 ms | **4.22×** (15 / 3.55 = 4.225) | 14.5× |
| p99 vs 50 ms | 1.99 ms | 12.97 ms | 6.500 ms | **3.85×** (50 / 12.97 = 3.855) | 25.1× |

**The floor is a property of this sample, not of CI.** Forty runs over five days
say the margin did not fall below 4.22× / 3.85× *in that window*. A later run
outside the range is news, not a contradiction — and the phrasing everywhere in
the tree now says so, because the alternative is what this issue is about.

### Why forty and not four

The first draft of this spec quoted **p50 1.98–2.29 ms, margin 6.6×–7.6×** from
four runs, and stated it as a property of the environment. It is falsified by
green runs the spec's own printed recipe returns:

| run | date (UTC) | p50 | margin | against the drafted 6.6× floor |
|---|---|---|---|---|
| 34586548968 | 2026-09-11 09:54 | 2.40 ms | 6.25× | below — and **more recent** than one of the four cited |
| 34535169347 | 2026-09-10 22:00 | 2.46 ms | 6.10× | below |
| 34475397402 | 2026-09-10 12:12 | 3.55 ms | 4.22× | far below |

The p99 range fared no better. The draft said **4.53–8.88 ms (5.6×–11.0×)**; the
forty-run sweep contains **12.97, 10.51, 10.41 and 9.32 ms** above it and
**1.99 and 3.51 ms** below it — six of forty outside a four-run "range".

That is the same defect #2141's 0.85× had and the same one #2148 was opened to
correct: a figure quoted without the qualification that makes it true. Recorded
here rather than silently fixed, because a correction that hides having been
needed teaches nothing.

### The environment ratio

| | p50 | p99 |
|---|---|---|
| CI, 40 runs, mean | 2.176 ms | 6.500 ms |
| dev box, 3 runs (spec 123, Release, 2026-09-10), mean | 30.967 ms | 96.233 ms |
| ratio of means | **14.2×** | **14.8×** |

Stated in the tree as "≈ 14× (p50) and ≈ 15× (p99)". Taken on means, because the
two populations do not overlap and a ratio of endpoints gives four different
answers depending which endpoints are picked. This is the measured size of what
`BudgetsApplyHere` excludes — #1905's gate is not merely defensible, it is
understated.

## 3. The records corrected

| record | was | is |
|---|---|---|
| `NFR002_MqttConnectAuthTests.cs` `<remarks>` | off-CI figures only (spec 123) | plus a CI paragraph: 40 runs, the two floors, the ratio, and the 50 ms-vs-5 ms distinction |
| `specs/021-transactional-outbox/verification.md:212-221` | "**breached** its 15 ms budget (17.58 ms) … and passed after" | an off-CI reading where the assertion is inert — it could neither breach nor pass — plus the sampled CI figure |
| `specs/087-…/census.md:177` | `17.58 ms`, `0.85×`, `TIGHT — has gone red` | `1.03–3.55 ms` on CI, `≥ 4.22×` sampled, `COMFORTABLE`, both environments labelled |
| `specs/087-…/census.md` §2d item 7 | p99 among the nine UNKNOWNs | struck in place, figures given; items 8 and 9 keep their numbers |
| `specs/087-…/spec.md:166`, `:168`, `:346` | same margin row; p99 in the "never recorded" row; F14 as a live inversion | corrected row; p99 removed from UNKNOWN; **F14 void, not deleted** |
| `specs/087-…/tasks.md:134` | F14's restatement | matches the void row |
| `specs/087-…/spec.md:146-149`, `tasks.md:129-131` | "nine of sixteen" / "F13 nine budgets", unqualified | one clause each: eight from 2026-09-11. **Annotated, not renumbered** |
| `specs/008-identity/plan.md:421-423` + `:429-445`, `tasks.md:234-235` | "asserts p99 ≤ 5 ms … against Testcontainers" | dated correction: p50 ≤ 15 ms, p99 ≤ 50 ms, Aspire fixture (ADR-0103) |

Three stale or wrong line cites in this spec's own artefacts were also corrected
— `ci.yml:179`→`:180`, `087/spec.md:165`→ the UNKNOWN row's real line, and the
`.cs` coordinates the added comments moved — with a line-shift table in
`spec.md` covering the rest. Precedent: `2a84c552 docs(specs): 087 — census's
line-shift note corrects the wrong cites`.

## 4. Build and tests

```
dotnet build -c Release          Build succeeded.  0 Warning(s)  0 Error(s)   (1m 34s)
dotnet test tests/Architecture.Tests --no-build -c Release
                                 Passed!  Failed: 0, Passed: 388, Skipped: 0  (3s)
```

## 5. What this note does **not** verify

Listed at length, because the issue being closed is one of a figure quoted
without its qualification.

- **`NFR002_MqttConnectAuthTests` has not been run on this branch, anywhere.**
  Not locally — it needs a booted Aspire stack, and off-CI the two thresholds
  are withheld anyway, so a local run would produce a dev-box number that this
  very issue exists to stop people quoting. Not on CI either, at the time of
  writing: **this branch's own `ci.yml` integration run is the pending "after"
  observation.** Until it is green, the phase-4a characterisation is discharged
  by the invariant hash in §1 and by forty runs of the *unmodified* file, not by
  an observed after-run.
- **The 40 CI figures were produced by the `develop` tip, not by this branch.**
  They are the "before" column by construction. That is sound only because §1
  shows no executable line changed; it would not be sound for any other kind of
  change.
- **No figure here is a measurement of NFR-002.** NFR-002 is ≤ 5 ms p99 auth
  overhead on production hardware (`specs/008-identity/spec.md:425`, ADR-0100).
  Nothing in this repository measures it, on any hardware. The CI p99 recorded
  above is wall-clock CONNECT→CONNACK through a container host proxy on a shared
  runner, and the whole point of the change is that the two are not comparable.
- **The dev-box column is three runs**, inherited from spec 123 and not
  re-measured here. The ≈ 14× / ≈ 15× ratio is therefore only as good as those
  three readings — it is an order-of-magnitude statement, not a calibration.
- **`docs/adr/0100-mosquitto-go-auth.md:237-239` is still wrong.** It says this
  test asserts p99 ≤ 5 ms against Testcontainers; it asserts p50 ≤ 15 ms and
  p99 ≤ 50 ms against the Aspire fixture. Amending an ADR is one of the
  autonomous lane's three blocked outcomes (ADR-0144), so it is recorded in
  `spec.md` *Out of scope, argued* and needs its own issue and a human. The two
  ordinary records carrying the same claim were corrected.
- **Nothing mechanically enforces any prose claim in this change.** No guard
  test asserts these markdown files contain these strings, deliberately: such a
  test proves a string was typed, not that a figure is true. §1's hash is the
  only automatic assertion this change has, and it covers the `.cs` file alone.
- **Artifact retention is 14 days.** After **2026-09-25** none of the forty runs
  above can be re-downloaded. The figures are in this file for that reason; the
  run ids will by then be unverifiable rather than merely inconvenient.
