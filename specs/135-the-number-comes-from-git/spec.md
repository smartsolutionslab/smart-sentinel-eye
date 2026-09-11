# Spec 135 — The number comes from git

**Issue:** #2177 — *Two of the three spec-number sources can never match a branch in
this repo, so the number is read off a possibly-stale working tree — it produced a
duplicate 084*
**Branch:** `fix/2177-the-number-comes-from-git`
**ADRs:** 0037 (phases and gates), 0144 (the autonomous lane), 0036 (smallest change),
0139 (red first), 0086 (no `Co-Authored-By`), 0028 (GitFlow — the branch naming this
turns on)

## The defect, restated

`Get-NextBranchNumber` (`.specify/scripts/powershell/create-new-feature.ps1`) took the
maximum of three sources:

```powershell
$highestBranch = Get-HighestNumberFromBranches   # git branch -a
$highestRemote = Get-HighestNumberFromRemoteRefs # git ls-remote --heads
$highestSpec   = Get-HighestNumberFromSpecs -SpecsDir $SpecsDir   # the working tree
return ([Math]::Max($highestBranch, $highestSpec)) + 1
```

Both branch sources delegate to `Get-HighestNumberFromNames`, which matches
`^(\d{3,})-`. **No branch in this repository has ever matched it.** ADR-0028 names
branches `fix/2111-…`, `docs/2124-…`, `chore/2141-…`, `test/198-…` — the digits are a
GitHub *issue* number and are never the first path segment. So both return 0, always,
and the spec number came entirely from `Get-HighestNumberFromSpecs`: a listing of the
**working tree's** `specs/` directory.

A working tree is behind whenever the checkout is. `origin/develop` carries the
correction for the case where it was:

- `5a4c0646 docs(specs): 084 — the index cannot see the state it must filter on`
- `b32d9e17 docs(specs): 086, not 084 — two in-flight specs already hold that number`

## Two populations of numbers the working tree cannot see

The fix has to cover both, and covering only one reintroduces the duplicate from the
other direction.

**Merged specs, after their branch is deleted.** `delete_branch_on_merge` is on, so
once a spec merges its branch is gone and the *only* surviving record of its number is
the `specs/NNN-*` directory in `develop`'s tree. A checkout 101 commits behind does not
have that directory, and mints the number again.

**In-flight specs, on pushed but unmerged branches.** Their `specs/NNN-*` directory
exists on that branch's tree and nowhere else — not in `develop`, not in any current
checkout. The issue's suggested remedy, `git ls-tree -d --name-only origin/develop
specs/`, does not see them. On several occasions during the 2026-09-09/10/11 run it
would have under-reported for exactly this reason: 110, 111 and 124 were each held by a
pushed branch while `develop` knew nothing of them.

So the source added here reads `specs/` **from every local branch and remote-tracking
ref**, not from `origin/develop` alone. Rationale and cost are in plan.md.

## The defect is routed around, not latent

Every spec delivered during the 2026-09-09/10/11 run derived its number **by hand**.
Each brief carried a line of the form *"123 is merged; 110, 111 and 124 are held by
pushed branches — expect 125; check for a duplicate (issue #2177)."* The workaround was
correct every time, and correct because a human ran `git ls-tree -d --name-only
origin/develop specs/` and then checked the remote branches.

**The script was never consulted.** This spec makes the script do what those briefs
did, so the next person who does consult it is not handed a number that is already
taken.

## Scope

**In:** one new function in `create-new-feature.ps1`, its inclusion in the maximum, a
comment recording that branch names here never carry the spec number, and an executable
guard.

**Out, deliberately:**

- **Removing the branch scanners.** The issue is explicit that they stay: they cost
  nothing and would start working if naming changed. What changes is that they are no
  longer allowed to *stand for* coverage they do not provide — a comment now says so.
- **Changing branch naming** so the scanners would match. That is a convention change
  and would need an ADR amending 0028.
- **The bash sibling.** `.specify/scripts/` has no bash `create-new-feature.sh` in this
  repo; there is nothing to keep in step.
- **The timestamp escape.** `^\d{8}-\d{6}-` stays excluded everywhere, including in the
  new source, and the guard asserts it.

## Phase 4a — red

Declared **behaviour-changing**, so 4a is a **red**, and the red is a real one rather
than an assertion about the file's source text.

A test asserting that `Get-NextBranchNumber`'s body contains the string `ls-tree` would
prove the file says something, not that the number it returns is right. This session
has already surfaced ten such cannot-fail assertions; this is not an eleventh.

Instead the guard builds throwaway git repositories — a bare origin, a `work` clone
deliberately left behind `origin/develop`, and a second clone that pushes what `work`
cannot see — runs the **real script** under PowerShell, and asserts the `FEATURE_NUM`
it returns. The stale checkout minting a number `develop` already holds is exactly the
condition the issue describes, and it reproduces in four seconds.

Verbatim red and green are in verification.md.

## Success criteria

- **SC-1** A checkout whose working `specs/` is behind `origin/develop` returns the
  next number after `develop`'s highest, not after its own.
- **SC-2** A number held only by a pushed, unmerged branch is not handed out again.
- **SC-3** A `specs/20260101-120000-*` directory anywhere in git still does not become
  the sequential number.
- **SC-4** The branch scanners are still called and still contribute their maximum.
- **SC-5** The whole thing runs under Windows PowerShell 5.1 (`pwsh` 7 is not on PATH
  on the delivering machine) and under `pwsh` on the ubuntu CI runner.

## Latency

**N/A.** No runtime code; this is a developer script.
