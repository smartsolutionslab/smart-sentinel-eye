# Plan 187 — A pin the model resolves

**Spec:** `specs/187-a-pin-the-model-resolves/spec.md`
**Issue:** [#2270](https://github.com/smartsolutionslab/smart-sentinel-eye/issues/2270)
**Branch:** `2270-manifest-tag-guard` (from `origin/develop` @ `e818acf8`)

---

## 1. Bounded context and layers

**None.** This feature touches no bounded context and no Domain, Application,
Infrastructure or Api layer. It lives entirely in the composition root and the
repository's own guard machinery:

| File | Role | Layer |
|---|---|---|
| `tests/Integration.Tests/AppHostContainerImagePinTests.cs` | **new** — the guard that reads the composed model | test |
| `tests/Architecture.Tests/ContainerImagePinTests.cs` | the source scan, renamed to say what it scans | test |
| `src/AppHost/AppHost.cs` | two comments naming the wrong authority (`:159`, `:293`) | composition root |

**Entities, value objects, invariants: none.** No aggregate, no `IValueObject<T>`,
no `Identifier`. Constitution §II is not engaged: nothing here is a domain model,
and `PrimitiveBoundaryTests` has nothing new to judge.

**Messaging: none.** No domain event, no integration event, no
`Shared.Contracts` message, no Wolverine handler. The handler-deconstruction
rule has no subject.

**Boundary rules:** unaffected. No `src/` project reference is added or moved,
so NetArchTest's cross-context rules are untouched. The single project reference
this plan relies on — `tests/Integration.Tests → src/AppHost`
(`SmartSentinelEye.Integration.Tests.csproj:24`,
`IsAspireProjectResource="false"`) — **already exists** and is what `AspireFixture`
and four existing `AppHost*Tests` classes are built on.

---

## 2. US1 — the guard that reads the composed model

### 2.1 Shape

A new class beside the one it copies:

```
tests/Integration.Tests/AppHostContainerImagePinTests.cs
  [Trait("Category", "FixtureLogic")]
```

The trait is load-bearing twice over:

- `ci.yml`'s **backend** job selects `--filter "Category=FixtureLogic"` and runs
  it with no Docker, on the Release build that job already produced. **No CI
  change is needed and none is made.**
- `IntegrationTestSelectionTests` **fails the build** for any class under
  `tests/Integration.Tests` carrying neither the Aspire collection nor a
  category trait. Omitting the trait is not a slow test; it is a red build.

The class composes the model exactly as `AppHostMediaMtxImageTests` does —
`DistributedApplicationTestingBuilder.CreateAsync<Projects.SmartSentinelEye_AppHost>(args)`,
`using`, **no `StartAsync`**. Nothing starts, so nothing is pulled and nothing
contends with a live stack on the same machine.

Reuse the two argument arrays that class already declares (run-mode parameters;
the same plus `E2ETests=true`). They are private to it today. **Copy them
rather than extracting a shared helper** — ADR-0036, no speculative generality:
two classes with four literal parameter values each is not yet an abstraction,
and a shared fixture would couple two guards whose populations deliberately
differ. Say so in a comment so the duplication reads as chosen.

### 2.2 The population, and how it is partitioned

For every resource in the model, for every `ContainerImageAnnotation` on it:

- **Judged** when the resource carries **no** `DockerfileBuildAnnotation` — it
  is pulled from a registry, so what its tag resolves to is decided upstream.
- **Excluded** when it carries one — `mosquitto` (`AppHost.cs:244`,
  `AddDockerfile`). Nothing is pulled for it; its tag is content addressed by
  Aspire (`mosquitto:97a8246…`, observed). Judging it would fail the build over
  a string no registry ever sees.

**The exclusion is by annotation, never by resource name.** A name list is a
narrowing that grows silently and is exactly the move ADR-0144 blocks; an
annotation test states the reason and admits the next `AddDockerfile` resource
automatically.

### 2.3 What "pinned" means — the same definition, deliberately

A reference is **pinned** when either:

- `SHA256` is non-null (a digest pin — representable, unused today), **or**
- `Tag` is non-null, non-empty, and **no hyphen-separated segment equals
  `latest`**, case-insensitively.

That last clause is `ContainerImagePinTests.IsFloating`, reproduced exactly.
`latest`, `latest-ffmpeg`, `latest-ffmpeg-rpi` fail; `4-management-alpine`
passes.

**This is the one place the plan must not be clever.** Widening the definition
would fail the build on `rabbitmq:4-management-alpine` on the first run, and
every route back to green is forbidden: pinning RabbitMQ is a decision the lane
may not make, and weakening the new guard is a blocked outcome. Spec §9 records
the residual gap and recommends a separate issue. The new guard's job is to
apply the *existing* rule to the *real* population — one change, not two.

An absent or empty `Tag` is treated as floating. It cannot occur through
`WithImage` (which assigns `"latest"`), but a guard whose answer depends on an
upstream `??` chain staying as it is today should not have a hole where that
chain changes.

### 2.4 The facts

Four, named sentence-style (ADR-0053):

| # | Fact | What it proves |
|---|---|---|
| F1 | `Every_container_image_a_run_mode_stack_composes_is_pinned` | the headline verdict, over the largest population |
| F2 | `Every_container_image_the_integration_fixture_composes_is_pinned` | the shape CI's own integration lane boots |
| F3 | `The_run_mode_population_includes_the_containers_a_published_manifest_omits` | the positive control **and** the justification for §3's rejected alternative |
| F4 | `A_locally_built_image_is_not_judged_against_a_registry_tag` | the exclusion is real and is annotation-shaped |

**F3 is the control, and it is not optional.** A guard whose population is empty
passes having checked nothing — the failure mode `ContainerImagePinTests` guards
against with `pins.Length.ShouldBeGreaterThan(0, "the scan is broken, not the
code")`, and the one spec 185 spent a whole feature on. F3 asserts a floor **and
names `fixture-video` and `camera-sim` explicitly**, because those two are
precisely what a publish-mode manifest drops (spec §2.1). If the argument for
this design is ever wrong, F3 is where it breaks.

The floor is `ShouldBeGreaterThanOrEqualTo`, not equality — a new container must
not fail a guard about tags. Measured today: **8** judged containers in run mode
(`mediamtx`, `fixture-video`, `camera-sim`, `postgres`, `pgadmin`, `rabbitmq`,
`keycloak`, `minio`) plus `mosquitto` excluded; **6** in the fixture shape
(`pgadmin` and `camera-sim` are both gated behind `isRunMode && !isE2ETests` and
are dev-only).

### 2.5 The failure message

Must be pasteable into `docker pull` and must name the resource. Follow
`AppHostMediaMtxImageTests.Reference`:

```
image.SHA256 is null ? $"{image.Image}:{image.Tag}" : $"{image.Image}@sha256:{image.SHA256}"
```

…prefixed with `Registry` when it is set, because `minio` resolves through
`quay.io` and a message that omits it sends the reader to the wrong registry.
The message should say *why* this fails — a floating tag changes what runs on
the next pull with no diff and no PR (#2103) — and should state that the source
scan cannot see this case, so the next reader does not go looking for a second
red that will never come.

### 2.6 Why not the issue's own suggestion

Recorded in the plan as well as the spec, because it is the decision the issue
asked the architect to make.

| Option | Verdict |
|---|---|
| `Architecture.Tests` + `--publisher manifest` | **Rejected.** Publish mode omits `fixture-video` and `camera-sim` (`AppHost.cs:16`, `:201`, `:675`; measured — the published manifest holds 5 images and neither name). A replacement that judges fewer containers than the blind guard it replaces is the narrowing #2270 forbids. It also drags a build + publish into a project that runs in seconds and deliberately holds no AppHost reference. |
| A `ci.yml` step | **Rejected.** Same population problem, plus the verdict becomes unreachable locally — the opposite of the direction spec 185 moved one. |
| `Integration.Tests` + in-process model | **Chosen.** Order-immune (reads the annotation `WithImage` writes), sees package-supplied coordinates, sees run-mode-only resources, Docker-free, already selected by an existing CI filter, and a copy of a pattern already in the tree. |

---

## 3. US2 — the source guard says what it checks

### 3.1 The renames

| Now | After |
|---|---|
| `No_container_image_in_the_app_host_runs_a_floating_tag` | `Every_literal_image_tag_written_in_the_app_host_is_pinned` |

The other two facts (`The_media_mtx_containers_all_name_one_release`,
`Every_written_media_mtx_reference_names_the_release_the_containers_run`) are
already accurate about their own subject and **do not change**.

The **class** name stays `ContainerImagePinTests`. `DatabaseCommandLogLevelTests.cs:44`
carries a `<see cref="ContainerImagePinTests"/>` and `docs/adr/0147` names it in
prose; renaming the class would break a cref and stale an ADR for no gain.

### 3.2 The documentation

The class doc gains a short, load-bearing paragraph saying what the scan cannot
do, and pointing at the guard that can:

- it reads **literals**, so a call whose coordinates come from a hosting package
  is invisible to it (`keycloak` today);
- it has **no notion of call order**, so a `WithImage` after a `WithImageTag`
  overwrites a pin it still matches and still passes (#2270);
- `AppHostContainerImagePinTests` is the authority for the composed claim; this
  scan remains the authority for what is **written**, including the MediaMTX
  references in `scripts/` and in prose, which no application model contains.

### 3.3 The characterisation obligation

The three facts are green on `develop` and must be green after, **assertions
unmodified**. Capture them green before touching the file; re-run after. An
assertion that has to be edited is evidence the scan's verdict moved — block,
do not adjust (spec §6). Pre-declared exemption: method names change, and
message text quoting a changed method name changes with it.

### 3.4 The two AppHost comments

- `AppHost.cs:159` — *"ContainerImagePinTests fails the build when any of this
  drifts apart."* Still true of the three MediaMTX literals. Add the model guard
  beside it, since it is what now makes the *resolved* claim.
- `AppHost.cs:293` — *"ContainerImagePinTests reads literals out of this file,
  so minio was the one container it could not see."* This one is now the wrong
  authority: the paragraph continues *"Order matters: `WithImage` re-parses the
  reference…"*, and the named guard is exactly the one that cannot check it.
  Name `AppHostContainerImagePinTests` as what enforces the order.

Comments only. **No executable line of `AppHost.cs` changes**, and the phase-5
check is that the measured references in spec §1.4 are byte-identical before and
after.

---

## 4. Role assignments (ADR-0144)

| Phase | Agent | Why |
|---|---|---|
| 4a | **test-writer** | Writes the four facts, runs them, returns verbatim output. Also runs the counterfactual (§5) and returns *that* output — it is the red. |
| 4b | **infra-engineer** | The subject is Aspire composition semantics, the run-mode/publish-mode split and CI job placement. Its brief covers AppHost wiring and CI; **backend-engineer** was considered and rejected — there is no bounded context, no EF/Wolverine, no domain model, and nothing on its checklist to apply. |
| 6 | **infra-reviewer** | Required. It owns AppHost wiring and "whether a green CI run actually proves anything", which is the entire subject. |
| 6 | ~~backend-reviewer~~ | **Skipped.** The diff is C# and xUnit, but holds no domain surface: no `Result`/`Option`, no value object, no guard clause, no persistence. Its checklist would be empty. |
| 6 | ~~security-reviewer~~ | **Skipped, with the one point it would have raised recorded instead.** An unpinned image *is* a supply-chain exposure, but this change moves no tag, adds no trust boundary, touches no secret, scope or token, and does not revisit spec 113's tag-over-digest decision. The residual exposure — floating-within-major, `4-management-alpine` — is named in spec §9 with a recommendation to file it separately, so it is not lost by the skip. |

---

## 5. The counterfactual, specified

Phase 4a's red. Run **after** the new facts are written and observed green on
the unmodified tree, because "green then red then green" is what proves the
guard discriminates rather than merely runs.

1. In `src/AppHost/AppHost.cs`, swap lines `:301` and `:302` so `WithImage("minio/minio")`
   follows `WithImageTag("RELEASE.2025-09-07T16-13-09Z")`.
2. Run the new class. **Expect F1 and F2 to FAIL**, each naming `minio` and a
   reference ending `:latest`. Capture verbatim.
3. Run `ContainerImagePinTests` against the same broken tree. **Expect it to
   PASS.** Capture verbatim. This is the issue's claim, re-proven on this tree,
   and the justification for US2's rename in one line of output.
4. `git checkout src/AppHost/AppHost.cs`. Re-run both. **Expect green.**

**Stop conditions, so a wrong red is not mistaken for the right one:**

- **If step 2 passes, STOP and report.** Either the annotation is not what the
  model resolves, or the swap did not take. No amount of editing the guard fixes
  that, and a guard that cannot catch its own filed counterfactual must not ship.
- **If step 3 fails, STOP and report.** The source scan catching the swap
  contradicts the issue's premise and US2's rename would then be wrong.
- **If F3 fails at any point, STOP and report.** The population is not what this
  plan measured, and every other verdict in the class is then unreliable.

`restoring-a-file-keeps-its-old-timestamp` applies at step 4: a `git checkout`
can leave MSBuild believing the build is current. Force the rebuild (touch the
file or build without `--no-build`) before reading step 4's green.

---

## 6. Risks

| Risk | Mitigation |
|---|---|
| A package-supplied image floats today and F1 is red on the first run | Measured before planning: all eight judged containers are pinned under the existing definition. If one is not, that is a **finding, not a guard to soften** — report it and file it. |
| `CreateAsync` needs Docker | It does not. `AppHostMediaMtxImageTests` carries the same trait and runs in the backend job, which has no Docker step. Building the model starts no resource. |
| The class runs twice (backend job **and** the integration job's exclusion filter) | Already true of `AppHostMediaMtxImageTests`. Accepted; it costs seconds and no container. Changing the filters is out of scope. |
| Two model compositions add wall-clock to the Docker-free step | Bounded: the existing class already composes twice. Measure in phase 5 and record the figure. |
| The guard is read as covering *every* floating reference | It does not cover floating-within-major. Spec §9 says so, F-message wording says so, and the follow-up is recommended rather than assumed. |
| Spec number collides with a parallel worktree | Re-checked immediately before commit, per `spec-number-check-origin-develop-isnt-enough`. |

---

## 7. Constitution and ADR alignment

- **§Testing / ADR-0139.** New behaviour starts red; the red is produced by
  counterfactual because the tree is correct. The characterisation half is
  declared separately and captured green first.
- **§IV (latency).** N/A — no production assembly changes behaviour.
- **ADR-0103.** No Testcontainers, no new Docker dependency; the Aspire model is
  composed in-process.
- **ADR-0036.** Three files. No shared fixture extracted, no CI step added, no
  new abstraction, no widening of the rule.
- **ADR-0144.** No ADR written, no constitution amended, no guard weakened, no
  scope narrowed. Phase 4a colour declared at phase 3 (spec §6).
