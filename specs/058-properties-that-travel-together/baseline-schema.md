# Baseline: what "unchanged" means for feature 058

**Feature**: 058 | **Recorded**: 2026-09-02 | **Base**: `develop` @ `104e859`

Recorded **before** any change, so that later verification compares against
something rather than against an assumption.

---

## T001 — Pending model changes, per context

`dotnet ef migrations has-pending-model-changes --project src/<Context>/Infrastructure --no-build`

| Context | Baseline |
|---|---|
| AuditObservability | **CLEAN** |
| Automation | **CLEAN** |
| CameraCatalog | **ERROR** — see below |
| EventIngestion | **CLEAN** |
| Identity | **CLEAN** |
| LayoutComposition | **CLEAN** |
| OverlayDesigner | **CLEAN** |
| StreamDistribution | **CLEAN** |
| SystemVariables | **CLEAN** |

**So the verification rule for this feature is the simple one**: after a slice,
its context must still report *no* pending model change. Anything at all in the
diff is this feature's doing. A column turning **nullable** means the
`Navigation(...).IsRequired()` line is missing.

### The first version of this file said the opposite, and was wrong

It recorded seven contexts as PENDING with an `AlterColumn` on `version`, and
built a verification rule around tolerating that noise. **Both the table and
the rule were artefacts of a measurement mistake**, and the mistake is worth
keeping because it is easy to repeat:

```sh
dotnet build SmartSentinelEye.slnx -c Release      # built Release
dotnet ef migrations has-pending-model-changes ... --no-build   # reads DEBUG
```

`dotnet ef --no-build` reads the **Debug** output. The Debug binaries on this
machine had been built while checked out on `057-typed-at-the-boundary`, so
every context was measured against **that branch's code** while this branch was
checked out. The `version` drift it reported is real — but it belongs to
`e23e882` on PR #2021, not to `develop`. Recorded as a correction on issue
**#2022**.

**The lesson for anyone verifying a slice**: build Debug (or pass a matching
`--configuration`) *on the branch you are measuring*, and be suspicious of a
result that does not follow from the change you just made. The empty diff after
slice 1, where a `version` change was expected, is what exposed this.

### CameraCatalog cannot be checked here

```text
Could not load file or assembly 'System.Runtime, Version=10.0.0.0,
Culture=neutral, PublicKeyToken=b03f5f7f11d50a3a'.
```

The project builds; only the EF design-time load fails, with and without
`--no-build`, on both branches. **CameraCatalog is slice 2** — before that slice
starts, either the tooling problem is solved or that slice's schema evidence
must come from CI. Do not let it pass unverified in silence.

CI run 33623647778's integration job separately shows every context service
`FailedToStart` behind `migrations: Finished (exit code 134)`. Whether that
shares a root cause with this design-time failure is unknown.

---

## T002 — Coverage baseline

`scripts/coverage-check.ps1` requires PowerShell 7; this machine has 5.1.
Figures are collected per slice with the manual reportgenerator route in
[quickstart.md](./quickstart.md).

| Assembly | Gate | Baseline | After slice |
|---|---|---|---|
| StreamDistribution.Domain | ≥ 90% | 95.8% (CI, run 33622840219) | *slice 1 — see verification* |

Spec 057 tripped this gate with two new types in Automation.Domain, so each
composite ships with its tests in the same task, and **any composite member
without a caller is deleted rather than tested**.

---

## T003 — Sites lacking a round-trip test

Audited per slice, immediately before the change it guards.

| Site | Covering test for both halves? | Action |
|---|---|---|
| `Stream` (provisioning) | **No** — `StreamTests` asserted `ProvisionedAt` only; nothing asserted `ProvisionedBy` on the aggregate | `Provision_records_when_it_happened_and_who_did_it` added **first**, on the old shape, and observed green before the composite existed (FR-009) |
| `Camera` (registration) | **Yes** — `CameraAddressChangeTests` asserts both halves survive an address change | none needed |
| `RegisteredClient` (registration) | **No** — `RegisteredClientTests` asserted `RegisteredAt` only | `Register_records_when_it_happened_and_who_did_it` added **first**, on the old shape, and observed green (FR-009) |

---

## Slice 2 note — CameraCatalog was verified with a borrowed line

CameraCatalog's schema evidence for slice 2 was obtained with
`Microsoft.EntityFrameworkCore.Design` **temporarily** added to its csproj, then
reverted. That reference is the real fix for the design-time load failure above,
and it lands on `develop` with PR #2021 rather than being duplicated here — two
branches adding the identical line would collide on rebase.

So on this branch, as committed, `dotnet ef` still cannot load CameraCatalog.
After #2021 merges and this branch rebases, it can, and the check should be
re-run without borrowing anything.

---

## Local limitations affecting all evidence

- **No Docker**: integration (`AspireFixture`) and e2e suites cannot run here.
  CI is the gate for FR-008's outward shapes.
- **PowerShell 5.1**: `scripts/coverage-check.ps1` cannot run.
- **`dotnet ef migrations remove` reaches for the database** and fails here.
  Delete the generated files and `git checkout` the snapshot instead.
- **`dotnet ef --no-build` reads Debug output.** See above. This one has already
  produced one wrong record.
