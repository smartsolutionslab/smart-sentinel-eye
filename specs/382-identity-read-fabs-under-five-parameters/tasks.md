# Tasks 382: Identity's read-fab resolution under five parameters

**Spec**: [spec.md](./spec.md). **Plan**: [plan.md](./plan.md). **Issue**: #2975 (feature-level;
no per-task issues).

**Phase 4a colour: CHARACTERISATION, observed green** (behaviour-preserving). T000a first adds the
list-route characterisation tests (commit `C0`), observed green on unchanged code; T001 then runs
the suite on that code and its output is quoted verbatim in the PR. The same commands must pass
after T010 with `git diff C0 -- tests/` empty. An assertion that would have to change is a
**block**, not an edit (ADR-0144).

**Engineer:** backend-engineer (T010). **Test-writer:** T000a (adds tests and extends the host,
spec §4, D-3) and T001 (runs only). **Security-sensitive:** T010 reshapes the helper that decides
which fabs a caller may read; Phase 6 runs `/security-review` (security-reviewer) as well as
`/code-review`.

**Scope is 1 of the issue's 8 hits** (H8). H1-H7 are blocked on the owner decision recorded on
#2971 (spec §6) and **must not be attempted**: each is reachable only by changing a route handler's
parameter list, which edits `CameraCatalogRouteTableCharacterisationTests` or
`IdentityRouteTableCharacterisationTests`.

**Forbidden in every task after T000a:** any file under `tests/`; any route handler's signature
(including the three `List` handlers and H1-H7); any CameraCatalog file; the `*Services` records;
any body change in `ResolveReadFabsAsync` beyond the signature, one deconstruction line and the one
doc reference; moving an error-code literal out of a handler body; a new or renamed file under
`src/*/Api`; ADRs, the constitution, CLAUDE.md; suppressions, thresholds, `.editorconfig`,
`SonarLint.xml`, `Directory.Build.props`, csproj files.

Legend: `[P]` = owns files disjoint from the other `[P]` tasks of its phase (ADR-0109). No
foundational task: nothing touches Shared.Kernel, Shared.Contracts or AppHost. There is no
parallelism inside the change: the helper's signature and its three callers must change in one
commit to build (ADR-0087). Commits land sequentially (one branch, one index).

---

## Phase 1: Baseline (orchestrator; no files)

- [x] **T000** On `38eb836f`, run spec §1's Release `--no-incremental` build and record the S107
  list under `src/(CameraCatalog|Identity)/Api` (the 8 of spec §1; 110 solution-wide) and the full
  Sonar list there (S107 only). Done at Phase 1; re-run by T020 for the after-figure.

## Phase 2: Characterisation, observed green (test-writer)

- [ ] **T000a** [US1] Extend `tests/Architecture.Tests/IdentityEndpointHost.cs` with the three list
  query handlers over a seedable, async-capable in-memory `IRegisteredClientQuerySource` (plan §4;
  a new fake file in `tests/Architecture.Tests` is allowed, it is not a test class). Add the six
  spec §2 scenarios per route to `DeviceAndKioskEndpointCharacterisationTests` (`/devices/`,
  `/kiosks/`) and `WebhookRotationEndpointCharacterisationTests` (`/webhook-integrations/`). No
  existing assertion changes; no new test class. Observe the new tests **and** the existing 34 plus
  both route-table tests green on unchanged production code; run the three plan §4 counterfactuals
  and report each red. Commit `test(identity): pin the list routes' fab resolution before the
  helper is reshaped` = `C0`. Depends on T000.

- [ ] **T001** [US1] Run every plan §4 "In-process" command (and "Aspire fixture" if the stack is
  free) on `C0`. Capture each summary line verbatim, with non-zero counts. Writes no file. Depends
  on T000a.

## Phase 3: The change (backend-engineer; one commit)

- [ ] **T010** [US1] `src/Identity/Api/IdentityFabResolution.cs` + `DevicesEndpoints.cs` +
  `KiosksEndpoints.cs` + `WebhookRotationEndpoints.cs`: add `FabReadRequest` after the class;
  `ResolveReadFabsAsync` takes `(FabReadRequest read, IFabAuthorizationGuard fabGuard, string
  unusableFabErrorCode, CancellationToken cancellationToken)` and deconstructs `read` first, the rest
  of its body byte-identical; the `<paramref name="fabId"/>` becomes a `<see cref>` to
  `FabReadRequest.FabId`; each `List` passes `new FabReadRequest(user, fabId)`, one argument per
  line, its error-code literal unchanged (plan §2.1). Remove any redundant `using` in the touched
  files only (`dotnet format style --diagnostics IDE0005`, scoped). One commit:
  `refactor(identity): read-fab resolution takes the caller's read request`. Depends on T001.
  **Security-sensitive.**

## Phase 4: Verify (orchestrator)

- [ ] **T020** [US1] Re-run T001's commands unmodified; quote the summaries. `git diff --stat C0 --
  tests/` empty. Release `--no-incremental` build: S107 in scope is exactly H1-H7 (H8 gone), 109
  solution-wide, and no S138, S104, S1541, S134 or other new warning in either Api project. Attach
  `git diff 38eb836f -- src/Identity/Api/IdentityFabResolution.cs` to the PR for the security
  review. Depends on T010.
- [ ] **T021** Comment on #2975: H8 cleared; H1-H7 are route handlers in two contexts whose
  route-table baselines pin their parameter lists (spec D-1), so they wait on the #2971 decision
  (options a-c). The PR uses `Refs #2975`, not a closing keyword; the issue stays open. Depends on
  T020.

## Dependencies

T000 → T000a → T001 → T010 → T020 → T021. Strictly sequential; nothing is `[P]`.
