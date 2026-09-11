# Tasks 135 — The number comes from git

Declared at phase 3: **behaviour-changing** → phase 4a is a **red**, observed by
running the real script against a deliberately stale checkout. Engineer: infra.

- [x] **T1** — Derive this spec's number the way the briefs have: `git ls-tree -d
      --name-only origin/develop specs/` for the merged population, then the same over
      every remote branch for the in-flight one. Record both figures.
- [x] **T2** — Write `scripts/create-new-feature.test.mjs`: throwaway git fixtures, the
      real script under PowerShell, assertions on the returned `FEATURE_NUM`. Three
      cases — stale checkout (SC-1), number held only by a pushed branch (SC-2),
      timestamp directory still excluded (SC-3).
- [x] **T3** — Run it and capture the **verbatim red**. Do not write the fix first.
- [x] **T4** — Add `Get-HighestNumberFromGitSpecDirs`: `for-each-ref` over `refs/heads`
      + `refs/remotes`, dedupe by commit SHA, `ls-tree -d --name-only <sha> specs/`,
      reuse `Get-HighestNumberFromNames` so the timestamp escape is defined once.
- [x] **T5** — Fold it into `Get-NextBranchNumber`'s maximum. Leave both existing
      sources in place (SC-4) and record why `Get-HighestNumberFromSpecs` stays.
- [x] **T6** — Comment on `Get-HighestNumberFromNames`: branch names here never carry
      the spec number, the scanners are kept but are contributing nothing, and the
      fetch is not doing the work it appears to.
- [x] **T7** — Re-run the guard green, and the whole `test:guards` suite with it.
- [x] **T8** — Run the **fixed script** against this real repository and check it
      returns the number T1 derived by hand. That is the honest end-to-end test.
- [x] **T9** — `verification.md`: verbatim red, verbatim green, and the real-repo run.

Not done, deliberately: the branch scanners are not removed; branch naming is not
changed; the timestamp escape is not touched; no ADR; no CI change (the existing
`test:guards` glob already picks the guard up).
