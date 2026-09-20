# Spec 187 — A pin the model resolves

**Issue:** [#2270](https://github.com/smartsolutionslab/smart-sentinel-eye/issues/2270)
— *"`ContainerImagePinTests` reads source text, so a pin that resolves to
`:latest` still passes."* Labels `bug`, `agent:ready`, no `agent:blocked`.
**Already on Project #13** (status Todo) — `gh issue view 2270` reports
`projects: Smart Sentinel Eye (Todo)`, so **no `item-add` is needed**.

**Spec number.** `git ls-tree -d origin/develop -- specs/` shows **185** as the
highest merged. The number above it is **not** free: open PR
[#2459](https://github.com/smartsolutionslab/smart-sentinel-eye/pull/2459)
(`2287-playwright-trace-redaction`) already carries
`specs/186-a-trace-that-keeps-its-secrets/`. Every remote branch was checked for
a `specs/187*` directory and none holds one. This spec is **187**.

**Branch:** `2270-manifest-tag-guard`, cut from `origin/develop` @ `e818acf8`.

**Created:** 2026-09-20. Phases 1–3 of ADR-0037, run in the ADR-0144 autonomous
lane.

**ADRs referenced:**

- **ADR-0139** (rules that fail the build, not the review). The ADR this spec
  serves directly: the rule "no container in this stack runs a floating tag"
  *is* wired to the build — and the wiring answers a different question than the
  rule asks.
- **ADR-0103** (integration tests are Aspire-only; no Testcontainers). Why the
  application model, not a container, is the thing a test may compose here.
- **ADR-0036** (smallest possible change; no speculative generality).
- **ADR-0037** / **ADR-0144** (the phased workflow; the lane, its phase-4a
  colours, and the blocked outcome of weakening a guard).
- **ADR-0052** / **ADR-0053** (xUnit + Shouldly; sentence-style test names).
- **ADR-0109** (the `[P]` disjoint-file rule).
- **Spec 113** (`specs/113-the-image-that-defines-a-contract/`) — the spec that
  introduced the pin rule, chose version tags over digests, and wrote both
  guards this spec corrects.

**No new ADR is needed.** Nothing architectural is decided: ADR-0139 already
says a rule belongs in the build, spec 113 already decided *that* images are
pinned and *how* they are spelled. This spec changes only **what the build
reads to decide it**. It does not amend the constitution.

**Latency budget (§IV): N/A.** The diff lands in `tests/Integration.Tests/`,
`tests/Architecture.Tests/` and two comment blocks in `src/AppHost/AppHost.cs`.
No production assembly changes behaviour, no leg of `event arrival → overlay
rendered` is touched, and no leg's measurement status moves.

---

## 1. The defect, re-measured on this tree

The issue was filed against #2266's branch. Everything in this section was
**re-run on this worktree** (`origin/develop` @ `e818acf8`) on 2026-09-20,
because a spec that repeats a filed premise without checking it is the defect
this repository has named most often.

### 1.1 What the guard actually reads

`tests/Architecture.Tests/ContainerImagePinTests.cs` opens
`src/AppHost/AppHost.cs` as **text** and matches two regexes:

```csharp
AddContainer\(\s*"(?<name>[^"]+)"\s*,\s*"(?<image>[^"]+)"\s*,\s*"(?<tag>[^"]+)"
WithImageTag\(\s*"(?<tag>[^"]+)"
```

Every tag it finds is checked by `IsFloating` — a tag floats when any
hyphen-separated segment equals `latest`. Three facts rest on that scan.

The scan has **no notion of call order**, and no notion of a call it does not
match. `WithImage("minio/minio")` is not matched by either regex, so the guard
cannot see that the call exists, let alone where it sits.

### 1.2 Why order decides the answer

Aspire's `ContainerResourceBuilderExtensions.WithImage(reference)` ends:

```csharp
imageAnnotation.Tag = parsedReference.Tag ?? tag ?? "latest";
```

A `WithImage` given a reference with no tag and no `tag` argument **assigns
`latest`**. So `WithImage(...)` after `WithImageTag("RELEASE.…")` overwrites the
pin, and the literal `"RELEASE.…"` the guard matched is still sitting in the
file, still passing, describing a tag nothing resolves.

`src/AppHost/AppHost.cs:300-303` is written in the correct order **and says so
in a comment** (`:294-299`: *"Order matters: `WithImage` re-parses the reference
and resets the tag to `latest` when it is given none, so it must come before
`WithImageTag`"*). That comment is, today, the entire enforcement. The guard
whose name is `No_container_image_in_the_app_host_runs_a_floating_tag` is not
part of it.

### 1.3 The counterfactual, as filed

On #2266's branch, with the two calls deliberately swapped:

- the generated manifest composed **`quay.io/minio/minio:latest`**;
- `ContainerImagePinTests` **passed 3/3**.

Not reproduced again here — reproducing it is **phase 4a's red** (§6), and
running it now would consume the evidence the lane requires be produced by the
test-writer and quoted in the PR.

### 1.4 The second blind spot, measured

The scan can only judge coordinates **written in `AppHost.cs`**. Measured
against the running stack (`docker ps`, persistent containers from a prior run
of this tree):

| Container | Image actually resolved | Visible to the source scan? |
|---|---|---|
| `mediamtx` | `bluenviron/mediamtx:1.21.0-ffmpeg` | yes — `AddContainer(…, "1.21.0-ffmpeg")` |
| `postgres` | `timescale/timescaledb:2.27.1-pg17` | yes — `WithImageTag` |
| `rabbitmq` | `rabbitmq:4-management-alpine` | yes — `WithImageTag` |
| `minio` | `quay.io/minio/minio:RELEASE.2025-09-07T16-13-09Z` | yes — `WithImageTag`, **but see §1.2** |
| `keycloak` | `quay.io/keycloak/keycloak:26.6` | **no** — supplied by `Aspire.Hosting.Keycloak` |
| `mosquitto` | `mosquitto:97a8246…` (locally built) | **no** — `AddDockerfile`, not pulled |

`keycloak` is pinned, and pinned by a package version this repository controls
through `Directory.Packages.props`. That is fine — and it is fine **untested**.
A package bump that moved it to a floating tag would land green. This is the
same gap #2266 had to close by hand for `minio`, one resource at a time.

### 1.5 Why this is the repository's own recurring defect

The guard reads the **design artefact** — the source text — and its green is
read as a statement about **behaviour**. That is
`guards-that-read-the-design-artefact`, named in this repository's own memory
after spec 050's realm-file test certified a claim that was false of every
running realm. The standing remedy is `prove-a-guard-by-counterfactual`, which
disproved three guards' own claims in a day. This is the fourth.

**The guard is not wrong about what it checks.** It is wrong about what its name
and its green are read to mean. Both halves of that sentence produce a task in
§3.

---

## 2. The decision the issue left open, and the answer

The issue offers two homes for a truthful guard and explicitly leaves the choice
open: `Architecture.Tests` with a publish step, or a CI step. **Both are
rejected, and a third — already present in this repository — is chosen.**

### 2.1 The manifest was published, and it is not sufficient

Run on this worktree, 2026-09-20:

```
$ dotnet run --project src/AppHost -- --operation publish --publisher manifest \
    --output-path <tmp>/manifest.json
info: Aspire.Hosting.DistributedApplication[0]
      Aspire AppHost version: 13.5.3+b5f143315ffb6968ea939a9978797a5b20e4c688
info: Aspire.Hosting.Publishing.ManifestPublisher[0]
      Published manifest to: <tmp>/manifest.json
exit=0
```

It works, exactly as the issue says. It also composes **fewer containers than
the guard must cover**. The manifest holds five pullable images —

```
postgres   docker.io/timescale/timescaledb:2.27.1-pg17
rabbitmq   docker.io/library/rabbitmq:4-management-alpine
keycloak   quay.io/keycloak/keycloak:26.6
mediamtx   bluenviron/mediamtx:1.21.0-ffmpeg
minio      quay.io/minio/minio:RELEASE.2025-09-07T16-13-09Z
```

— plus `mosquitto` as `"type": "container.v1"` with a `build` block and **no
`image` key at all**. `fixture-video` and `camera-sim` **are absent**.

They are absent for a structural reason, not an accidental one.
`AppHost.cs:16` reads `bool isRunMode = builder.ExecutionContext.IsRunMode;`,
and `fixture-video` (`:201`) and `camera-sim` (`:675`) sit behind it. A manifest
publish is **publish mode**, so a manifest-reading guard can never see them.

That is decisive: two of the three MediaMTX containers — both of which the
*current, blind* guard does see — would fall out of a manifest-based
replacement. A fix that narrows the population it judges is the blocked outcome
the issue names, arrived at by a different door.

### 2.2 A CI step is rejected for a weaker reason, but a real one

A `ci.yml` step running the publish and grepping the JSON would inherit §2.1's
population problem, and would additionally put the verdict where only CI can
reach it: a developer swapping the two calls locally sees green until push. This
repository has already moved one verdict *out* of the Docker job for that reason
(spec 185, `IntegrationTestSelectionTests`). Moving one in is the wrong
direction.

### 2.3 The chosen home already exists

`tests/Integration.Tests/AppHostMediaMtxImageTests.cs` **already composes the
application model in-process and reads `ContainerImageAnnotation`** — the very
annotation `WithImage` overwrites. It:

- carries `[Trait("Category", "FixtureLogic")]`, so it already runs in
  `ci.yml`'s **backend** job (`--filter "Category=FixtureLogic"`), with no
  Docker and no new CI step;
- composes the model twice, under run-mode args and fixture args, so it sees
  `fixture-video` and `camera-sim` — and asserts their presence today;
- builds the model only (`CreateAsync` starts nothing), so it costs no
  container and is safe beside a live stack.

The new guard is the same shape, asking a different question. Nothing new is
introduced: no publish step, no CI change, no YAML, no Docker dependency, no
build-order surprise beyond the `Integration.Tests → AppHost` project reference
that has existed since the fixture did.

**This is the same move the memory note prescribes:** ask the system, not the
record. The application model is what Aspire hands to DCP and to the manifest
writer alike; a tag read out of it is the tag that gets pulled.

---

## 3. User stories

### US1 — *A floating tag cannot reach the composed model* (P1)

**As** the engineer who bumps or re-registers a container in `AppHost.cs`,
**I want** the build to fail when any container the model composes resolves to a
floating or absent tag,
**so that** the green I get means "nothing in this stack floats" rather than
"nothing that floats was typed as a literal in this file".

Independently shippable: this story alone closes the issue. US2 changes no
verdict.

### US2 — *The source guard says what it checks* (P2)

**As** the next reader of `ContainerImagePinTests`,
**I want** its name and its documentation to describe the scan it performs,
**so that** its green is not read as a claim about what runs — the misreading
that is the whole of #2270's second half.

Ships after US1 because its documentation names the new guard.

---

## 4. Acceptance scenarios (Gherkin)

### US1

```gherkin
Scenario: The happy path — the stack as it stands today
  Given the application model composed from AppHost.cs on develop
  When every container resource that is pulled is inspected
  Then each resolves to a tag with no "latest" segment, or to a digest
  And the guard reports no floating reference
```

```gherkin
Scenario: The conflict — a pin overwritten by call order (#2270's counterfactual)
  Given minio's WithImage("minio/minio") is moved after WithImageTag("RELEASE...")
  When the application model is composed
  Then minio's ContainerImageAnnotation.Tag is "latest"
  And the guard FAILS, naming minio and the reference it resolved to
  And ContainerImagePinTests still passes — which is the point
```

```gherkin
Scenario: The bad-request case — a package-supplied image starts floating
  Given a hosting package bump moves keycloak's default tag to "latest"
  And no literal for it exists anywhere in AppHost.cs
  When the application model is composed
  Then the guard FAILS, naming keycloak
```

```gherkin
Scenario: The empty-scan case — the guard cannot pass by finding nothing
  Given a change that removes every container from the model, or breaks composition
  When the guard runs
  Then it FAILS on the population floor
  And the message says the scan is broken, not that the code is clean
```

```gherkin
Scenario: A locally built image is not judged
  Given mosquitto is declared with AddDockerfile and carries DockerfileBuildAnnotation
  When the guard partitions the model's container resources
  Then mosquitto is excluded, because nothing is pulled from a registry for it
  And the exclusion is by annotation, never by resource name
```

```gherkin
Scenario: The run-mode-only resources are inside the population
  Given the model composed with run-mode arguments
  When the guard lists the containers it judges
  Then fixture-video and camera-sim are among them
  And this is asserted explicitly, because a publish-mode manifest omits both
```

**Auth/scope: N/A.** No endpoint, no token, no scope, no fab. Stated rather than
omitted: the spec template asks for an auth scenario and there is genuinely none
to write.

### US2

```gherkin
Scenario: The rename changes no verdict
  Given ContainerImagePinTests' three facts are green on develop
  When the facts are renamed and re-documented
  Then the assertions are byte-identical to their previous text
  And all three are still green, unmodified
```

---

## 5. Independent end-to-end test procedure

Runnable by a human with no knowledge of the diff, on a checkout of the branch.
No Docker required.

```sh
# 1. The guard passes on the tree as written.
dotnet test tests/Integration.Tests/SmartSentinelEye.Integration.Tests.csproj \
  --filter "FullyQualifiedName~AppHostContainerImagePinTests" \
  -- RunConfiguration.TreatNoTestsAsError=true
# expect: Passed, > 0 tests executed

# 2. Break it the way #2270 describes: swap the two minio calls in
#    src/AppHost/AppHost.cs (:301 and :302).
# 3. Re-run step 1.
# expect: FAILED, message naming `minio` and `quay.io/minio/minio:latest`

# 4. Re-run the old guard against the same broken tree.
dotnet test tests/Architecture.Tests/SmartSentinelEye.Architecture.Tests.csproj \
  --filter "FullyQualifiedName~ContainerImagePinTests"
# expect: Passed — unchanged, and now documented as such rather than trusted

# 5. git checkout src/AppHost/AppHost.cs && re-run step 1. Passed.
```

Step 4 is not decoration. It is the assertion that the fix **added** a verdict
rather than moved one, and that US2's rename tells the truth.

---

## 6. Phase-4a colour: **RED**, with one characterisation half

Declared here because ADR-0144 requires the architect to declare it at phase 3
and resolves ambiguity to red.

**US1 is RED.** It is behaviour-changing: a tree that the build accepts today
(the order-swapped one) stops being accepted. A test arriving green is a phase-4
failure.

**The red must be produced by counterfactual**, because the tree is currently
*correct* — `AppHost.cs:300-303` has the calls in the right order, so a
correctly-written guard passes on the first run. That is not a green test; it is
a test with nothing to catch yet. The red is obtained by applying #2270's own
swap, capturing the verbatim failure, and reverting. `prove-a-guard-by-counterfactual`
is this repository's named remedy and the technique that found the issue; a
guard shipped without it is a guard whose claim nobody checked, which is
precisely what is being fixed.

**US2 is CHARACTERISATION, observed green.** `ContainerImagePinTests`' three
facts are green today and must be green after, with their **assertions
unmodified**. A rename and a doc rewrite change no verdict. An assertion that
has to be edited is evidence the scan's answer moved — **block, do not adjust**.
The exemption, pre-declared so it is not mistaken for drift: the test *method
names* change, and any failure-message text that quotes a changed method name
changes with it. `ShouldBeEmpty` / `ShouldBe` / `ShouldBeGreaterThan` calls and
their subjects do not.

---

## 7. Scope

**In:**

1. `tests/Integration.Tests/AppHostContainerImagePinTests.cs` — new;
   `[Trait("Category", "FixtureLogic")]`; composes the model under run-mode and
   fixture arguments and asserts every **pulled** container image is pinned.
2. `tests/Architecture.Tests/ContainerImagePinTests.cs` — renames and
   documentation only; assertions untouched.
3. `src/AppHost/AppHost.cs` — two comment lines (`:159` and `:293`)
   that currently name `ContainerImagePinTests` as the enforcement of a claim it
   does not enforce.

**Out, deliberately:**

- **Deleting or narrowing `ContainerImagePinTests`.** Explicitly a blocked
  outcome (#2270, ADR-0144). It also still earns its keep: it is the only check
  on the MediaMTX references written in `scripts/` and in prose, which no
  application model contains.
- **Widening what counts as floating.** `rabbitmq:4-management-alpine` floats
  *within major 4* and is deliberately not caught today —
  `ContainerImagePinTests.IsFloating`'s own doc says narrowing that "is a
  decision about a different image, and this guard was not given one". The new
  guard uses the **same** definition. Changing it is a supply-chain decision
  affecting five images and belongs in its own issue; see §9.
- **Pinning by digest.** Spec 113 chose version tags, with reasons. Not
  revisited.
- **Any `ci.yml` change.** The chosen home is already selected by an existing
  filter. Adding a step would be a second moving part for the same verdict.
- **Any change to what is pinned.** No tag moves in this spec. The measured
  references in §1.4 must be byte-identical before and after.
- **A publish-mode assertion.** Considered and rejected in §2.1; recorded as
  considered, not forgotten.

---

## 8. Locked technology choices

| Concern | Choice | Source |
|---|---|---|
| Test framework | xUnit + Shouldly | ADR-0052 |
| Test naming | sentence-style with underscores | ADR-0053 |
| Model composition | `DistributedApplicationTestingBuilder.CreateAsync<Projects.SmartSentinelEye_AppHost>(args)` — build only, no `StartAsync` | existing `AppHostMediaMtxImageTests` |
| Annotations read | `ContainerImageAnnotation` (`Registry`/`Image`/`Tag`/`SHA256`), `DockerfileBuildAnnotation` | verified present in `Aspire.Hosting` 13.5.3 |
| Job placement | `[Trait("Category", "FixtureLogic")]` → `ci.yml` backend job, Docker-free | ADR-0103, spec 185 |
| Argument guards | `Ensure.That(...)` where any are needed | ADR-0105 |

---

## 9. Follow-up this spec deliberately does not do

**Floating-within-major is still unguarded.** `4-management-alpine` resolves to
a different RabbitMQ patch release on the next pull, with no diff and no PR —
the exact failure mode spec 113 was written against, one level up. This spec
does not close it, because doing so would fail the build on day one and the only
ways to reach green would be to pin RabbitMQ (a decision, not a fix) or to
weaken the new guard (a blocked outcome). **Recommend filing a separate issue.**
Named here so the gap is recorded rather than inherited silently by the next
reader of a now-truthful guard.
