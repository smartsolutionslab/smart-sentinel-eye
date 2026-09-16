# Plan — Spec 166, a hang that fails loudly

**Phase:** 2 (Plan) — ADR-0037
**Issue:** [#2409](https://github.com/smartsolutionslab/smart-sentinel-eye/issues/2409) · **Branch:** `fix/2409-a-hang-that-fails-loudly`
**Spec:** `spec.md`
**Engineer:** `infra-engineer` · **Reviewer:** `infra-reviewer`
**Phase 4a colour: BEHAVIOUR-CHANGING (RED first)** — `spec.md` §5. The
red/green pair is constructed against a deliberately-hanging throwaway test,
not against the real (unreproducible-on-demand) hang.
**Latency (§IV): N/A** — no production runtime file changes.
**New ADR required: no** — `spec.md` §4.

---

## 1. Which engineer, and why

**`infra-engineer`.** Every file in scope is CI/build tooling:
`.github/workflows/ci.yml` and `scripts/coverage-check.ps1`. No `src/*` or
`apps/*` file changes. The judgement needed — vstest/`dotnet test` CLI
semantics, sizing a CI watchdog timeout against measured job timing, keeping
a high-contention file (`ci.yml`, per ADR-0109/CLAUDE.md) to the smallest
possible diff — is the `infra-engineer` brief's territory exactly (it already
owns "CI workflows" and "the AppHost is the composition root" adjacent
tooling). `backend-engineer` was considered and rejected: §2 of the spec
found a candidate lead inside `StreamDistribution.Infrastructure.Tests`, but
the spec explicitly does **not** act on it (spec.md §2.3, §5 — "no test ...
changes shape"). If a future slice *does* act on that lead (e.g., because the
next hang's dump confirms it), that slice is backend work and gets its own
issue/spec; this one is CI-instrumentation only.

## 2. The change, in one sentence

Add vstest's built-in hang watchdog (`--blame-hang`,
`--blame-hang-dump-type mini`, `--blame-hang-timeout 3min`) to the two
`dotnet test` call sites in the `backend` CI job that currently run without
one, so a hung test assembly fails the build within minutes, names the hung
test, and writes a thread dump — instead of the job running silent until the
20-minute `timeout-minutes` kills it and reports `cancelled`.

## 3. Exact call sites

### 3.1 `scripts/coverage-check.ps1`

Current loop (`foreach ($proj in $testProjects)`), building `$testArgs`:

```powershell
$testArgs = @(
    'test', $proj.FullName
    '-c', $Configuration
    '--collect:XPlat Code Coverage'
    '--results-directory', (Join-Path $rawDir $projectName)
)
if ($NoBuild) { $testArgs += '--no-build' }
```

Add the three blame flags to this array (order does not matter to vstest, but
keep them grouped and comment why, per this repo's own "say why at the call
site" convention already used for `RetryEveryMethod()`):

```powershell
$testArgs = @(
    'test', $proj.FullName
    '-c', $Configuration
    '--collect:XPlat Code Coverage'
    '--results-directory', (Join-Path $rawDir $projectName)
    # #2409: a hung test host previously ran silent until the job's
    # 20-minute ceiling killed it and reported `cancelled`, not `failure`.
    # blame-hang fails fast, names the stuck test, and writes a dump.
    '--blame-hang'
    '--blame-hang-dump-type', 'mini'
    '--blame-hang-timeout', '3min'
)
```

**Verify interaction with `--collect:XPlat Code Coverage`:** both are vstest
data-collector/diagnostic features and are documented as combinable, but this
has not been run in this repo — confirm locally (or in a scratch CI run)
before merging that coverage collection still succeeds with blame enabled,
and that the coverlet instrumentation/restore behaviour `coverage-check.ps1`
already guards against (`CoverletDataCollectorException`, #1142) is
unaffected. This is a "must verify, not assume" item — flag it as such if it
surfaces anything unexpected rather than silently adjusting a threshold.

### 3.2 `.github/workflows/ci.yml` — "Docker-free fixture logic tests" step

```yaml
- name: Docker-free fixture logic tests
  run: |
    dotnet test tests/Integration.Tests/SmartSentinelEye.Integration.Tests.csproj \
      -c Release \
      --no-build \
      --filter "Category=FixtureLogic" \
      --blame-hang --blame-hang-dump-type mini --blame-hang-timeout 3min
```

Same three flags, same value, same reasoning — comment can reference the same
issue rather than repeating the full rationale (keep the diff small; a
one-line `# #2409: see coverage-check.ps1's comment` is enough).

### 3.3 Upload the dump on failure

A `--blame-hang-dump-type mini` dump is written to the test's
`--results-directory` (or `TestResults/` by default if none is set, which is
the case for the ci.yml step in §3.2). For it to be inspectable after a real
CI hang, it must survive the job. Two existing `upload-artifact` steps are
candidates:

- `coverage-check.ps1`'s projects already write into
  `artifacts/coverage/raw/<projectName>/`, which is **not** currently
  uploaded (only `artifacts/coverage/report` is, via the "Upload coverage
  report" step). A hang dump written into that per-project raw directory
  would be silently discarded.
- No existing step uploads `**/TestResults/*.trx`-adjacent artifacts for the
  `backend` job (unlike `integration`, which does — `ci.yml`'s "Upload
  integration test results" step, `path: '**/TestResults/*.trx'`).

**Decision for this slice:** add one new `if: always()` upload step to the
`backend` job, mirroring `integration`'s, capturing the blame output:

```yaml
- name: Upload hang/crash diagnostics
  if: always()
  uses: actions/upload-artifact@ea165f8d65b6e75b540449e92b4886f43607fa02 # v4
  with:
    name: backend-blame-diagnostics
    path: |
      **/*.dmp
      **/*Sequence*.xml
    retention-days: 14
    if-no-files-found: ignore
```

The path is not scoped to `**/TestResults/` — verified against a deliberately
induced real hang that `coverage-check.ps1`'s loop writes into
`artifacts/coverage/raw/<project>/<guid>/` (it passes its own
`--results-directory`, so the vstest default `TestResults/` never applies
there). This replaces an earlier draft of this glob (`**/TestResults/*.dmp`)
that matched neither the real directory nor, once verified, the real
filename.

The sequence-file glob (`**/*Sequence*.xml`) is deliberately loose rather
than anchored to a prefix or suffix: the collector assembly we ran against
writes `Sequence_<guid>.xml` (prefix), confirmed the same way as the
directory above, but `dotnet test --help` in this repo's pinned SDK documents
the opposite — `<guid>_Sequence.xml` (suffix). The two disagree, and the help
text is exactly what a future reader checks first, so anchoring to either
spelling risks a "fix" into a glob that matches nothing real. Matching both
costs nothing here.

`if-no-files-found: ignore` matters: on every normal (non-hung) run this glob
matches nothing, and the step must not fail the job for that. This is new
scope beyond the two flag additions, but without it the instrument answers
"a hang happened" without answering "which test and why" on the very next
occurrence — which is the entire point named in the issue title. Kept minimal:
one step, no new job, no change to existing steps.

## 4. What is explicitly not touched

- `integration` job, `e2e` job — `spec.md` §3.2.
- Coverage thresholds, `$thresholds` table, any existing test file.
- `Directory.Build.props`, `Directory.Packages.props`, `global.json` — no
  new package reference; the blame flags are vstest platform flags, already
  present in the pinned SDK.
- No `.runsettings` file is introduced. Considered: a `.runsettings` with
  `<Hang>` config would centralise the settings, but there is no existing
  `.runsettings` anywhere in the repo (checked: `find . -iname
  "*.runsettings"` → none) and introducing the mechanism itself for a
  two-call-site, two-flag change is exactly the speculative generality
  Karpathy guidelines rule out. CLI flags at the two call sites are the
  smallest change that achieves this.

## 5. Risk / cost review (spec.md §3.3, restated as engineering checklist)

| Risk | Mitigation |
|---|---|
| A legitimately slow project is misidentified as hung | 3min budget vs. observed 3–14s normal per-project gaps (12–60× headroom); verify against a fresh timing run before merge (measurement runs need repeating) |
| `--blame-hang` interacts badly with `--collect:XPlat Code Coverage` | Verify locally per §3.1; this is the one genuinely unverified technical claim in this plan |
| The dump is written but discarded, so the instrument answers "yes" but not "which" | §3.3's upload step |
| Scope creep into `integration`/`e2e` | Explicitly declared out of scope, `spec.md` §3.2 |
| The mechanism itself ships unobserved (this is new CI behaviour) | Phase 4a's constructed counterfactual, `spec.md` §5 |

## 6. Bounded context / DDD sections — there are none

This is CI tooling. No aggregate, no domain event, no persistence, no HTTP
surface, no bounded context. Answered by explicit absence, matching the
convention spec 162 §1 set for a similarly non-domain slice.

## 7. Rollback

If `--blame-hang` proves incompatible with coverage collection (§3.1's open
verification) or produces false-positive hangs in CI despite the 3-minute
budget, the rollback is deleting the three flags from the two call sites and
the one upload step — no other file depends on their presence, no schema or
data is written, nothing to migrate back.
