# Plan 135 — The number comes from git

## Constitution / ADR check

| Concern | Verdict |
|---|---|
| ADR-0037 (gates) | No gate weakened, added or moved. Phases 1–3 produce these artifacts; 4a is declared red in spec.md. |
| ADR-0144 (lane) | No ADR written, no constitution amended, no gate weakened. Implementing a decision, not making one. |
| ADR-0028 (GitFlow) | **Read, not changed.** The branch naming that makes the two scanners dead is the convention; this spec adapts to it rather than altering it. |
| ADR-0036 (smallest change) | One new function, one changed `Math::Max`, two comments, one guard file. No refactor rides along; the existing sources are untouched. |
| Constitution §IV (latency) | N/A — a developer script, not on any runtime path. |
| §Testing | Behaviour-changing → red, observed. |
| ADR-0065 (coverage gates) | Untouched. No .NET code in the change. |

**No ADR is needed.** Nothing here decides anything; it makes one function read the
source of truth it was always meant to read.

## The design question the issue leaves open

The issue proposes `git ls-tree -d --name-only origin/develop specs/` as the fourth
source. That covers the *merged* population and misses the *in-flight* one, and the
in-flight one is not hypothetical: three numbers were held that way on the day the
issue was filed.

Three candidates were considered.

| Candidate | Sees merged | Sees in-flight | Cost | Verdict |
|---|---|---|---|---|
| `ls-tree origin/develop specs/` | yes | **no** | 1 process | Reintroduces the duplicate from the branch side. |
| `git log --all --diff-filter=A --name-only -- specs/` | yes | yes | full history walk | Also resurrects numbers from abandoned and renamed directories, and burns them. Slow and over-reports. |
| **`ls-tree specs/` over every local + remote-tracking ref** | yes | yes | one process per distinct commit | **Chosen.** |

The chosen one is also the literal mechanisation of the manual check the briefs have
been running: `develop`'s tree, plus the remote branches.

**Cost, measured in this repository:** 18 refs, deduplicated by commit SHA, 1.7 s
worst case on Windows — dominated by process spawn. The non-dry-run path already runs
`git fetch --all --prune` immediately before, which is slower. Deduplication by
`%(objectname)` is what keeps this proportional to distinct commits rather than to
ref count, and local branches usually share a commit with their remote twin.

**Remote-tracking refs, not `ls-remote`.** `ls-remote` returns SHAs, and a SHA whose
objects the checkout does not have cannot be `ls-tree`d. `refs/remotes/*` is what the
`git fetch --all --prune` on the non-dry-run path has just refreshed, and reading it is
side-effect-free — which is what `-SkipFetch` actually asks for.

**Local branches are included too.** They cost nothing extra (they usually dedupe
against their remote twin) and they cover a spec committed locally and not yet pushed.

## What changes

`.specify/scripts/powershell/create-new-feature.ps1`, three edits:

1. **`Get-HighestNumberFromGitSpecDirs`** (new). `for-each-ref` over `refs/heads` and
   `refs/remotes` with `%(objectname) %(refname)`; skip `*/HEAD`; dedupe by SHA;
   `git ls-tree -d --name-only <sha> specs/` per distinct commit; strip the `specs/`
   prefix and hand the names to the **existing** `Get-HighestNumberFromNames`. Reusing
   that function is deliberate: the `^(\d{3,})-` match and the `^\d{8}-\d{6}-`
   timestamp escape are then defined in exactly one place, so the escape cannot drift
   between sources. Wrapped in `try`/`catch` with `Write-Verbose`, mirroring the two
   existing git readers — a script that cannot reach git must still produce a number.

2. **`Get-NextBranchNumber`** takes it into the maximum:
   `[Math]::Max([Math]::Max($highestBranch, $highestSpec), $highestGitSpec)`. Both
   existing sources stay. `Get-HighestNumberFromSpecs` earns its place for one case
   nothing else covers — a `specs/NNN-*` directory created in the working tree and not
   yet committed — and that reason is now written next to it.

3. **The comment on `Get-HighestNumberFromNames`** records that branch names here carry
   an *issue* number in a later path segment, so the two scanners match nothing, and
   that the fetch they depend on is not doing the work it looks like it is doing. This
   is the issue's "do not let them stand for coverage they do not provide", written
   where the next reader will meet it.

## What verifies it

`scripts/create-new-feature.test.mjs`, a `node --test` guard alongside the two that
already live there (`lint-scope`, `wait-for-e2e-stack`). It is picked up by the
existing `test:guards` glob, which `pnpm test` runs and the **blocking** `frontend` CI
job runs — no CI change needed.

It builds real throwaway repositories in a temp directory and runs the real script:

- a bare `origin` whose `develop` carries `001`, `002`, `003` and a timestamp directory;
- a `work` clone that has **fetched but not merged**, so its tree still stops at `002`;
- a second clone that pushes `003`, and in one case a `fix/1234-delta` branch carrying
  `004`.

`work` is where the script is run, from `work`'s own copy of `.specify/scripts/powershell`
so `Get-RepoRoot` resolves there. `-DryRun -Json` means no branch and no directory is
created, and the assertion is on the returned `FEATURE_NUM`.

**PowerShell discovery:** `pwsh` first, then `powershell`. Windows here has 5.1 and no
`pwsh`; the ubuntu runner has `pwsh` and no `powershell`. The skip therefore fires on
neither machine that matters, which is the same reasoning the `wait-for-e2e-stack`
guard applies to `bash`.

## Risks

- **Ref count.** A repository with hundreds of remote branches pays a process per
  distinct commit. Mitigated by the SHA dedupe; if it ever bites, the fix is to
  restrict to `refs/remotes/origin` plus `refs/heads`, which is a one-line change.
- **Detached HEAD with no local branches.** Covered: `refs/remotes` still has develop,
  and the working-tree source still runs.
- **No git at all.** Unchanged — the caller already falls back to the working tree.
