# Verification 187 — A pin the composed model resolves (#2270)

**Latency: N/A.** Test-infrastructure guard; no leg of constitution §IV's
event→overlay path touched, no production assembly changed.

## Summary

`tests/Architecture.Tests/ContainerImagePinTests.cs` proves image tags are
pinned by matching literals in `src/AppHost/AppHost.cs`'s source text, and
has no notion of call order between `WithImage(...)` and
`WithImageTag(...)`. Aspire's `WithImage` resets the tag to `latest` when
given none, so a `WithImage` call running *after* `WithImageTag` silently
overwrites the pin in the composed application model while the old guard,
still matching the same literal it always matched, keeps passing. This was
proven by counterfactual on #2266's branch before this issue was filed —
not hypothetical.

The fix adds `tests/Integration.Tests/AppHostContainerImagePinTests.cs`,
which composes the real Aspire application model in-process
(`DistributedApplicationTestingBuilder`, no `StartAsync`) and reads the
actual `ContainerImageAnnotation` each resource carries — the same
annotation `WithImage` writes — so the order the calls ran in is already
baked into what gets inspected. Zero executable-code changes: `AppHost.cs`
gained two comment corrections naming the new guard; nothing else in it
changed.

## Phase 4a — the counterfactual, quoted verbatim (ADR-0139)

**T004 — baseline green**, unmodified tree, both guards:
```
Architecture.Tests ContainerImagePinTests: Total tests: 3, Passed: 3
Integration.Tests AppHostContainerImagePinTests: Total tests: 4, Passed: 4
```

**T005 — the red**, `AppHost.cs:301`/`:302` swapped so
`WithImageTag(...)` precedes `WithImage("minio/minio")`:
```
Passed AppHostContainerImagePinTests.The_run_mode_population_includes_the_containers_a_published_manifest_omits [492 ms]
Failed AppHostContainerImagePinTests.Every_container_image_the_integration_fixture_composes_is_pinned [100 ms]
Error Message:
 Shouldly.ShouldAssertException : floating
  should be empty but had
1
  item and was
[PulledImage { ResourceName = minio, Registry = quay.io, Image = minio/minio, Tag = latest, SHA256 = , Reference = quay.io/minio/minio:latest }]
Passed AppHostContainerImagePinTests.A_locally_built_image_is_not_judged_against_a_registry_tag [64 ms]
Failed AppHostContainerImagePinTests.Every_container_image_a_run_mode_stack_composes_is_pinned [59 ms]
Error Message: (same minio → quay.io/minio/minio:latest)
Total tests: 4
     Passed: 2
     Failed: 2
```

**T006 — the contrast**, same broken tree, the OLD guard:
```
Passed ContainerImagePinTests.The_media_mtx_containers_all_name_one_release [29 ms]
Passed ContainerImagePinTests.No_container_image_in_the_app_host_runs_a_floating_tag [2 ms]
Passed ContainerImagePinTests.Every_written_media_mtx_reference_names_the_release_the_containers_run [16 ms]
Total tests: 3
     Passed: 3
```
This is #2270's own claim, re-proven live on this tree: a floating tag
reaches the composed model and the guard that exists to prevent exactly
that reports green.

**T007 — reverted**, rebuilt (a restored file keeps its old mtime, so the
build must be forced), both guards green again — independently re-run by
the orchestrator after every subsequent change, most recently after the
phase-6 fix round (below).

## Phase 6 — one review round, no blockers, four should-fix findings

`infra-reviewer`'s pass **did not stop at reading the diff** — it
independently reproduced the mechanism itself, without editing this
repository: a standalone scratch console app pinned to this solution's
exact `Aspire.Hosting` 13.5.3 / `CommunityToolkit.Aspire.Hosting.Minio`
13.5.0 packages, composing `minio` both call orders and applying the new
guard's own `IsPinned` predicate verbatim — confirming `IsPinned=False`
under the swapped order and `IsPinned=True` under the correct one. It also
hashed `AppHost.cs` with every `//` line stripped, both before and after
the diff, and got an identical digest — an independent, non-textual proof
that the change really is comment-only, not merely a diff that reads that
way.

No blocker. Four should-fix findings, all fixed and independently
re-verified:

1. **F3's run-mode population floor was wrong** — asserted `>= 7`, but the
   real composed run-mode population is **8**: `pgadmin`
   (`docker.io/dpage/pgadmin4:9.15.0`, added via `.WithPgAdmin()` under
   `isRunMode && !isE2ETests`) was missing from the spec's own measurement,
   the plan, and the test's floor and failure message. A floor of `>= 7`
   against a true population of 8 meant any one container could silently
   fall out of the judged set and the control would not catch it —
   exactly the slack the control exists to remove. Fixed: floor raised to
   8, `pgadmin` added to the message, spec/plan figures corrected.
2. **F2 had no population floor at all**, though `tasks.md` T003 required
   one (`>= 6` fixture-mode). Fixed: added
   `ShouldBeGreaterThanOrEqualTo(6, ...)` naming the six fixture-mode
   containers.
3. **The `DockerfileBuildAnnotation` exclusion's doc comment was factually
   wrong** — it said "nothing is pulled from a registry" for mosquitto,
   but `src/AppHost/mosquitto/Dockerfile` pulls three base images
   (`debian:bookworm-slim` ×2, `golang:1.23-bookworm`), all floating
   within minor/patch. The exclusion *logic* was correct (mosquitto's own
   `ContainerImageAnnotation` really is `placeholder:latest`, and judging
   it would fail the build over a string no registry ever sees) — only the
   comment overclaimed. Fixed: reworded to say the base images are a
   separate, currently unguarded surface, and spec §9's follow-up-issue
   recommendation now names both this and the pre-existing
   `rabbitmq:4-management-alpine` floating-within-major gap.
4. **This `verification.md` did not exist yet** at review time. Written
   now, closing the gap.

Independently re-verified by the orchestrator after the fix commit
(`f3928705`): `git diff origin/develop --stat` confirms only the three
permitted files (`AppHostContainerImagePinTests.cs`, `spec.md`, `plan.md`)
changed beyond the already-approved `AppHost.cs`/`ContainerImagePinTests.cs`
edits; both guards re-run and green (below).

## Independent re-verification, this pass

```
$ dotnet test tests/Integration.Tests --filter "FullyQualifiedName~AppHostContainerImagePinTests"
Passed!  - Failed: 0, Passed: 4, Skipped: 0, Total: 4, Duration: 518 ms

$ dotnet test tests/Architecture.Tests --filter "FullyQualifiedName~ContainerImagePinTests"
Passed!  - Failed: 0, Passed: 3, Skipped: 0, Total: 3, Duration: 78 ms
```

`git diff origin/develop -- src/AppHost/AppHost.cs` — 2 comment blocks
changed (T010/T011), zero statements touched, independently re-confirmed
line-by-line by the orchestrator in addition to the reviewer's hash proof.

## T014 — cost

The full `Category=FixtureLogic` suite (149 facts across
`Architecture.Tests` and `Integration.Tests`, including the four new
facts): **4s test-execution time** (wall clock including build/restore was
49s, dominated by build, not test execution). The four new facts measured
in isolation: 518-637ms across repeated runs. Two app-model compositions
per full run (`AssertAllPinned`'s caller and `A_locally_built_image_...`'s
own `DistributedApplicationTestingBuilder.CreateAsync` call) account for
most of that — noted by the reviewer as a nit (F4 composes the model
twice; not fixed, since the class already runs in well under a second and
the duplication is legible, not a hidden cost). Not surprising enough to
warrant a design change.

## T015 — CI placement

Confirmed locally that the `[Trait("Category", "FixtureLogic")]` on the
new class is the same trait `.github/workflows/ci.yml`'s backend job's
"Docker-free fixture logic tests" step already filters on — the reviewer
ran that step's exact filter locally and observed the new facts execute
under it. **Reading an actual CI job log to confirm the same in the real
run is deferred until the PR opens and its checks run** — recorded here
so the gap is explicit rather than assumed closed; will be confirmed
before merge by reading the backend job's log directly.

## What was NOT independently reproduced

The phase-6 reviewer's own list, carried forward honestly rather than
re-asserted as closed:
- The reviewer reproduced the counterfactual's *mechanism* (the annotation
  really does become `latest` under the swapped order, via an isolated
  scratch app) but did not edit this repository's own `AppHost.cs` to
  re-run the new test against a broken tree — that specific run is
  phase 4a's (test-writer's), quoted verbatim above, and re-verified by
  the orchestrator by independent re-run of the *reverted* state only
  (the broken-tree run itself was not re-executed a third time, since
  doing so requires the same revert-and-forced-rebuild dance and the
  mechanism is now proven two independent ways: test-writer's real run,
  and the reviewer's isolated scratch-app reproduction).
- Whether a future hosting-package resource could add a
  `ContainerImageAnnotation` through a publish-only or start-time path
  was not tested (only `CreateAsync`→`BuildAsync` in run mode was
  checked). Recorded as a residual, not a defect — the same class of gap
  spec §9 already names for other reasons.

## Residuals accepted in writing

- **Floating-within-major tags** (`rabbitmq:4-management-alpine`,
  `keycloak:26.6`, `mediamtx:1.21.0-ffmpeg`, `minio`'s dated release tag,
  `pgadmin4:9.15.0`) are pinned by this guard's definition (a hyphen
  segment equal to `latest`) but can still drift within a minor/patch
  line. Widening the definition is a supply-chain decision affecting five
  images and is explicitly out of scope for this issue (spec §9
  recommends a separate issue).
- **`src/AppHost/mosquitto/Dockerfile`'s three base images**
  (`debian:bookworm-slim` ×2, `golang:1.23-bookworm`) are pulled from a
  registry but invisible to every guard in this repository, since none
  reads a Dockerfile. Newly surfaced by this review round; added to the
  same recommended follow-up issue as the floating-within-major gap above
  rather than fixed here, since fixing it is an unrelated, larger change
  (a Dockerfile-scanning guard is a different mechanism entirely).
- **`AppHost.cs` is an ADR-0109 contention file** and grew by comment
  lines only; any parallel branch touching it will still conflict on
  rebase. No action needed beyond normal rebase discipline.
