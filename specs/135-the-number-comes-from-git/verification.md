# Verification 135 — The number comes from git

**Phase 5, ADR-0037.** Every figure below is a command run on this branch, quoted
verbatim. The guard runs the real script against real git repositories, and the last
section runs the fixed script against **this** repository.

## The two dead sources, measured on this repository

Not inferred from the regex — the two functions were loaded out of
`origin/develop`'s copy of the script and called against this repo:

```
branches: 0
remoterefs: 0
worktree: 135
```

`git branch -a` and `git ls-remote --heads` contribute **0**, exactly as issue #2177
says, and the entire number came from the working tree. (`worktree: 135` rather than
`134` because `specs/135-…` had already been created by the time this was run.)

## Phase 4a — red, before the fix existed

`scripts/create-new-feature.test.mjs` run against the unmodified script:

```
✖ a checkout behind origin/develop does not mint a number develop already holds (4493.8102ms)
✖ a spec held only by a pushed unmerged branch is not handed out twice (5245.0192ms)
✖ a timestamp-prefixed spec directory does not become the sequential number (5031.2846ms)
ℹ tests 3
ℹ pass 0
ℹ fail 3

✖ failing tests:

test at scripts\create-new-feature.test.mjs:119:1
✖ a checkout behind origin/develop does not mint a number develop already holds (4493.8102ms)
  AssertionError [ERR_ASSERTION]: Expected values to be strictly equal:

  '003' !== '004'

    actual: '003',
    expected: '004',

test at scripts\create-new-feature.test.mjs:132:1
✖ a spec held only by a pushed unmerged branch is not handed out twice (5245.0192ms)
  AssertionError [ERR_ASSERTION]: Expected values to be strictly equal:

  '003' !== '005'

    actual: '003',
    expected: '005',

test at scripts\create-new-feature.test.mjs:153:1
✖ a timestamp-prefixed spec directory does not become the sequential number (5031.2846ms)
  AssertionError [ERR_ASSERTION]: Expected values to be strictly equal:

  '003' !== '004'

    actual: '003',
    expected: '004',
```

**`'003'` is the defect itself.** `origin/develop` carries `specs/003-gamma`; the
`work` checkout has fetched but not merged, so its tree stops at `002`, and the script
hands out `003` — a number that is already taken. That is the duplicate 084, reproduced
in four seconds from a fixture rather than from a 101-commit-stale checkout.

The second case is the half the issue's proposed remedy would have missed: `004` lives
only on `fix/1234-delta`, a pushed unmerged branch, and `origin/develop` alone cannot
see it.

## Green, after the fix

```
✔ a checkout behind origin/develop does not mint a number develop already holds (4314.4608ms)
✔ a spec held only by a pushed unmerged branch is not handed out twice (5534.6659ms)
✔ a timestamp-prefixed spec directory does not become the sequential number (5549.9559ms)
ℹ tests 3
ℹ pass 3
ℹ fail 0
```

The third case is what keeps the escape honest: `20260101-120000-legacy` and
`20260102-130000-another-legacy` are both in git, and `20260102` would swamp every
sequential number if `^\d{8}-\d{6}-` had been dropped. `004`, not `20260103`.

The whole guard suite, which is what the blocking `frontend` CI job runs via
`pnpm test` → `test:guards`:

```
✔ a checkout behind origin/develop does not mint a number develop already holds (5358.3645ms)
✔ a spec held only by a pushed unmerged branch is not handed out twice (6566.9589ms)
✔ a timestamp-prefixed spec directory does not become the sequential number (6441.9087ms)
✔ every TypeScript file under e2e/ is covered by the root ESLint configuration (3271.1392ms)
✔ the root lint script runs the e2e leg (1.3344ms)
✔ CI gates on the root lint and test scripts, not a re-spelled apps filter (1.1792ms)
✔ a stack whose databases carry applied migrations is reported ready (1545.5358ms)
✔ a stack whose migration run aborted partway is not reported ready (25261.4753ms)
✔ a stack the script cannot ask about migrations is not reported ready (14288.0173ms)
ℹ tests 9
ℹ pass 9
ℹ fail 0
```

Run under **Windows PowerShell 5.1** — `pwsh` 7 is not on PATH on this machine, so the
guard's `powershell` fallback is the path that was exercised here, and `pwsh` is the
path CI will exercise.

## The honest end-to-end test — does it agree with the hand-derived number?

The number for this spec was derived by hand, the way every brief in the
2026-09-09/10/11 run derived one:

```
$ git ls-tree -d --name-only origin/develop specs/ | tail -1
specs/134-the-label-means-what-it-says
$ for b in $(git for-each-ref --format='%(refname)' refs/remotes/origin); do \
    git ls-tree -d --name-only $b specs/; done | sort -u | tail -1
specs/134-the-label-means-what-it-says
```

Highest anywhere in git: **134**. Expected next: **135**.

The fixed script, run against this repository:

```
$ ./.specify/scripts/powershell/create-new-feature.ps1 -Json -DryRun -ShortName 'the-number-comes-from-git' 'the number comes from git'
{"BRANCH_NAME":"135-the-number-comes-from-git","SPEC_FILE":"D:\\Github\\smart-sentinel-eye\\specs\\135-the-number-comes-from-git\\spec.md","FEATURE_NUM":"135","HAS_GIT":true,"DRY_RUN":true}
```

**135 — the same number.** And the new source read it from git rather than from the
tree:

```
git spec dirs: 134
```

**One thing this run does not prove, and should not be read as proving.** This checkout
is current, so the old script would also have returned 135 — from the working tree,
which happens to agree. Agreement on a fresh checkout is the easy case and was never in
doubt. The fixture is where the two answers diverge, and there the old script returns a
number that is already taken. That divergence, not this agreement, is the evidence.

## What was not verified

- **`pwsh` 7.** Not installed on this machine. The guard's discovery tries `pwsh`
  first, so the ubuntu CI run will exercise it; if PowerShell 7 parsed any of this
  differently, the `frontend` job is where it surfaces. Nothing in the new function is
  5.1/7-divergent — `for-each-ref`, `ls-tree`, `HashSet[string]`, `-split`, `Trim`.
- **A repository with hundreds of remote branches.** Cost was measured here at 18 refs
  / 1.7 s worst case before SHA deduplication. The scaling risk is recorded in plan.md
  with its one-line remedy.

## Latency

**N/A.** A developer script; not on the event-to-overlay path or any runtime path.
