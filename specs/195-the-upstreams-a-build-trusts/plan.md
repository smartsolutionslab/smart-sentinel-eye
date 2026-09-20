# Plan 195 — The upstreams a build trusts

**Spec:** `specs/195-the-upstreams-a-build-trusts/spec.md`
**Issue:** [#2296](https://github.com/smartsolutionslab/smart-sentinel-eye/issues/2296)
**Branch:** `2296-pin-mosquitto-and-keycloak` from `origin/develop` @ `a61977a0`

---

## 1. Bounded context and layers

**None, and that is the plan's first load-bearing fact.** Nothing here has a
domain, an aggregate, a value object, a handler, a message, a migration or a
persisted row. There is no cross-context reference to make and no
`Shared.Contracts` message to version, so the boundary rules
(`ArchitectureTests`, NetArchTest, `PrimitiveBoundaryTests`,
`HandlerDeconstructionTests`) have nothing to say about this diff.

The whole feature touches **four files**:

| File | Layer, honestly | Change |
|---|---|---|
| `src/AppHost/mosquitto/Dockerfile` | build input; not compiled, not analysed, not referenced by any project | three `FROM` digests, one `ARG`, one `sha256sum -c`, three comments |
| `tests/Architecture.Tests/DockerfileUpstreamPinTests.cs` | **new**; source-scanning guard, no Docker, no Aspire, runs in milliseconds | new file |
| `src/AppHost/AppHost.cs` | Aspire composition root (ADR-0000 row 024) | three chained calls + a comment on the keycloak resource |
| `tests/Integration.Tests/AppHostContainerImagePinTests.cs` | composed-model guard (spec 187) | one new fact + a doc-comment correction |

`tests/Architecture.Tests/ContainerImagePinTests.cs` gets a **doc comment only**
(T013) — no assertion, no regex, no file-set change. That is the concrete form
of spec §7.1's decision.

**Latency budget: N/A.** No production assembly is in the diff.

---

## 2. US1 — the guard that reads a Dockerfile

### 2.1 Shape

`tests/Architecture.Tests/DockerfileUpstreamPinTests.cs`, public class, four
`[Fact]`s, sentence-style names (ADR-0053), Shouldly (ADR-0052). No trait: the
`Architecture.Tests` project is the Docker-free, seconds-long suite and this
class keeps it that way — it opens files, exactly as `ContainerImagePinTests`,
`GuardBanWiringTests`, `IntegrationTestSelectionTests` and
`FoundingDecisionRecordTests` do, and for the reason ADR-0103 gives: build
configuration leaves no trace in IL and composing a container to read a text
file would be absurd.

Private `RepositoryRoot()` walking up to `SmartSentinelEye.slnx`, **duplicated**
from the nine classes in this project that already carry one rather than extracted into a
helper. Nine copies of a five-line walk is the established convention here; the
tenth is not the one that earns an abstraction (ADR-0036), and spec 187 made the
same call for the same reason about its argument arrays.

Relative paths reported with `/`, normalised through
`Path.GetRelativePath(...).Replace(Path.DirectorySeparatorChar, '/')` — the
Windows-green/Linux-red trap this repository has already paid for once.

### 2.2 The population

Enumerated by **glob**, never by name:

```
Directory.EnumerateFiles(root, "Dockerfile*", SearchOption.AllDirectories)
```

excluding any path segment `node_modules`, `obj`, `bin`. Today that finds
exactly one file, `src/AppHost/mosquitto/Dockerfile`. A name constant would be
shorter and would silently stop covering the tree the day a second Dockerfile
appears; spec 187 rejected a resource-name list for the same reason and chose an
annotation. The enumeration is asserted non-empty with the message this
repository uses for every scan — *"the scan is broken, not the code. A guard
that matches nothing passes, and a passing guard that checks nothing is
indistinguishable from one that holds."*

### 2.3 What is parsed

Three line shapes, each a compiled `Regex`, each matched per line so a failure
can name a line number:

| Name | Pattern (intent) | Notes |
|---|---|---|
| `FromInstruction` | `^\s*FROM\s+(?<ref>\S+)(\s+AS\s+(?<stage>\S+))?` | case-insensitive: `FROM`/`from` are both legal Dockerfile |
| `DigestSuffix` | `@sha256:(?<digest>[0-9a-f]{64})\s*$` applied to `ref` | 64 lowercase hex, anchored — `@sha256:abc` is not a digest |
| `RemoteFetch` | `\b(wget|curl)\b` | on the **logical** instruction, see below |
| `ChecksumCheck` | `sha256sum\s+-c` | likewise |

**Line continuations are joined first.** A Dockerfile `RUN` spans lines with
trailing `\`, and the `wget` and the `sha256sum -c` will sit on different
physical lines. The scan therefore folds each instruction into one **logical
line**, remembering the physical line number the instruction started on. A
regex applied per physical line would report "a fetch with no checksum" for a
Dockerfile that has one — a false red, which is the fastest way to get a guard
deleted.

**A stage reference is not an upstream.** `FROM plugin_builder` or
`FROM mosquitto_builder AS x` names a stage built above, not a registry image,
and demanding a digest of it would be nonsense. The scan collects declared stage
names from the `AS` clauses it has already seen and **skips** a `FROM` whose
reference matches one. Today there are none; writing it is two lines and not
writing it is a landmine under the next multi-stage edit.

**Comments are stripped** (`^\s*#`) before any of this, so the header paragraph
explaining *why* the image is built from source cannot trip the scan by
containing the word `FROM` — which it does, at `Dockerfile:3`.

### 2.4 The four facts

| # | Name | Asserts |
|---|---|---|
| F1 | `Every_base_image_a_dockerfile_builds_from_is_pinned_by_digest` | every non-stage `FROM` reference ends in `@sha256:<64 hex>`. Failure lists `file:line → reference` |
| F2 | `Two_stages_that_build_from_one_image_name_one_digest` | group the pinned references by image-and-tag; every group has exactly one digest. Failure prints both lines and both digests |
| F3 | `Every_archive_a_dockerfile_downloads_is_verified_against_a_checksum` | every logical instruction matching `RemoteFetch` also matches `ChecksumCheck`. Failure names `file:line` and the fetched URL |
| F4 | `The_scan_reads_the_dockerfiles_this_repository_actually_has` | the positive control: ≥ 1 Dockerfile, ≥ 3 judged `FROM` references, ≥ 1 judged fetch. Floors are `ShouldBeGreaterThanOrEqualTo`, never equality — spec 187's rule, so removing a stage is a conversation and not a red build |

**No fact names a digest, a version, an image or a checksum.** This bans a
category, never a value — `ContainerImagePinTests`' own doctrine, and the reason
bumping Debian will not require editing the guard that governs Debian. F2's
"one digest per image" is the single exception-shaped assertion and it is still
a category: it compares the file against itself.

### 2.5 The failure messages

Each message says the file, the line, the offending text, and *what goes wrong
in the world* — the register the two existing guards use:

> `src/AppHost/mosquitto/Dockerfile:17 → debian:bookworm-slim` has no digest. A
> floating base image changes what this build produces on the next pull, on
> whichever machine pulls first, with no diff and no PR (#2265, #2296). Resolve
> it with `docker buildx imagetools inspect debian:bookworm-slim` and write
> `image:tag@sha256:…` — keep the tag, it is what tells a reader which release
> the digest is.

> `src/AppHost/mosquitto/Dockerfile:23` downloads
> `https://mosquitto.org/files/source/…` and never checks what arrived. The build
> trusts a third-party host's bytes with nothing to compare them against. Add
> `echo "<sha256>  <file>" | sha256sum -c -` to the same `RUN`.

### 2.6 Why not the issue's own two suggestions

Spec §7.1 is the argument; the plan records only its consequence: **no file in
`tests/Integration.Tests/AppHostContainerImagePinTests.cs` reads a Dockerfile,
and no regex in `tests/Architecture.Tests/ContainerImagePinTests.cs` learns
Dockerfile grammar.** Each guard keeps one surface, one grammar, one authority —
and T013 makes all three say so out loud.

---

## 3. US1 — the Dockerfile itself

### 3.1 The diff, precisely

```diff
 ARG MOSQUITTO_VERSION=2.0.18
+# Checksum of the release tarball, moved together with the version above.
+# Corroborated against Alpine aports' independently recorded sha512 for the same
+# file (spec 195 §6) — a hash computed from one's own download proves only what
+# arrived.
+ARG MOSQUITTO_SHA256=d665fe7d0032881b1371a47f34169ee4edab67903b2cd2b4c083822823f4448a

-FROM debian:bookworm-slim AS mosquitto_builder
+FROM debian:bookworm-slim@sha256:3783cc…251 AS mosquitto_builder
 ARG MOSQUITTO_VERSION
+ARG MOSQUITTO_SHA256
 …
 RUN wget -q "https://mosquitto.org/files/source/mosquitto-${MOSQUITTO_VERSION}.tar.gz" \
+    && echo "${MOSQUITTO_SHA256}  mosquitto-${MOSQUITTO_VERSION}.tar.gz" | sha256sum -c - \
     && tar xzf "mosquitto-${MOSQUITTO_VERSION}.tar.gz"

-FROM golang:1.23-bookworm AS plugin_builder
+FROM golang:1.23-bookworm@sha256:167053…4db AS plugin_builder

-FROM debian:bookworm-slim
+FROM debian:bookworm-slim@sha256:3783cc…251
```

Four points on that shape:

1. **The tag stays.** `image:tag@sha256:…` is longer than `image@sha256:…` and
   is the form to write: the digest is what Docker resolves, the tag is what
   tells a human it is bookworm and not trixie. The guard requires the digest and
   is indifferent to the tag; the reader is not.
2. **The digest literal is repeated, not hoisted into an `ARG`.** An
   `ARG DEBIAN_DIGEST` interpolated into both `FROM` lines would guarantee
   agreement — and would make F2 a tautology and force the guard to resolve
   `ARG` substitution to read a `FROM` at all. Two literals plus a guard that
   compares them keeps the check real and the parser simple. The duplication is
   the point.
3. **The checksum is an `ARG` beside the version, because they move together.**
   A hash is meaningless without the version it belongs to; adjacency is what
   stops a future bump from changing one and not the other. F3 only requires
   that the fetch is checked, so the `ARG` spelling is free.
4. **`sha256sum -c -` reads from stdin and exits non-zero on mismatch**, which
   fails the `RUN` and therefore the build. `sha256sum` ships in `coreutils`,
   present in `debian:bookworm-slim`.

### 3.2 The comment the Dockerfile gains

One short block above the first `FROM`, saying what spec §2 says: the base
images are digest-pinned, the apt archive deliberately is **not**, so this image
is *pinned, not reproducible* — and naming the follow-up issue so the next
reader meets the decision rather than discovering the gap. No drive-by comments
beyond that: ADR-0036, and the file already carries three substantial *why*
blocks that must not be disturbed.

---

## 4. US2 — the Keycloak coordinates

### 4.1 The diff

```diff
 var keycloak = builder
     .AddKeycloak("keycloak", adminPassword: keycloakPassword)
+    .WithImage("keycloak/keycloak")
+    .WithImageTag("26.6.4")
+    .WithImageRegistry("quay.io")
     .WithRealmImport("../AppHost/Realms");
```

plus a comment above `var keycloak`, in the register of minio's at
`AppHost.cs:288-296`: the package supplied all three coordinates and this file
named none; Keycloak decides realm import, scope issuance and `iss`, so a
package bump moves those with no diff here; `WithImage` resets the tag when
given none, so the order is fixed; and the tag `26.6.4` is the same manifest the
package's `26.6` resolved to on the day it was pinned (digest recorded in the
comment, so the next reader can check the claim instead of trusting it).

`.WithRealmImport(...)` stays **last**. It is unrelated to the image
coordinates, and moving it would put an unnecessary line in the diff of a file
two other guards read.

### 4.2 The new fact, and why it is a characterisation

Added to `AppHostContainerImagePinTests` — same subject, same composition
machinery, no new fixture, no new file:

```
The_keycloak_image_resolves_to_the_upstream_repository_this_stack_expects
```

It asserts that `keycloak`'s `ContainerImageAnnotation` has image
`keycloak/keycloak`, registry `quay.io`, and a tag that is present and not
floating. **It does not name the tag.** Naming the tag would make a Keycloak
bump edit its own guard, which is how a guard gets deleted within a month
(`FoundingDecisionRecordTests`' argument, quoted by `ContainerImagePinTests`).

This fact is **green before the change** — the package supplies exactly those
coordinates today — and must pass **unmodified** after. That is the
characterisation in the ADR-0144 sense, and it is the same construction
`AppHostMediaMtxImageTests` used for spec 113: assert the invariant, not the
value the change moves.

### 4.3 What the fact cannot cover, and what covers it instead

The fact says the coordinates are *right*; it cannot say the pin *changed
nothing*, because a single run sees one tree. That claim is established by
**observation**, T008's probe: before and after the change, a deliberately false
assertion prints the resolved reference, and the two printed strings are
compared. Identical strings are the evidence; they go in the PR body verbatim.
This is the repository's own `prove-a-guard-by-counterfactual` technique used for
its other purpose — making a passing run **say** what it saw.

---

## 5. The counterfactuals, specified

Three, each with a defined expected failure. Any one of them passing when it
should fail means the guard does not discriminate: **stop and report**, do not
adjust the guard to make the counterfactual behave.

| # | Construct | Must fail | Where |
|---|---|---|---|
| CF1 | The unmodified, unpinned Dockerfile | F1 (3 references), F3 (1 fetch) | T004 — free; it is the tree as it stands |
| CF2 | Pinned Dockerfile, second `debian` digest altered by one character | F2 only; F1 still passes | T006 |
| CF3 | Pinned Dockerfile, recorded SHA-256 altered by one character, `docker build` | the build, at the `sha256sum` step | T007 / phase 5 |

CF2 is the one that must not be skipped. F1 and F3 are proven by CF1 for free,
but F2 is green on both the broken tree and the fixed one unless someone
deliberately breaks *its* invariant — and an assertion nobody has seen fail is an
assertion nobody has shown to check anything. This repository found five
assertions that could not fail in one week.

---

## 6. Role assignments (ADR-0144)

| Phase | Agent | Why |
|---|---|---|
| 4a | **test-writer** | One new xUnit class and one new fact in an existing one. Writes tests only, runs them, returns verbatim output — including the natural red of CF1, which is this spec's strongest evidence |
| 4b | **infra-engineer** | The whole non-test diff is a Dockerfile and an Aspire resource declaration. No bounded context, no domain, no persistence, no message — **backend-engineer would be the wrong brief** and spec 187 made the same call for the same subject |
| 6 | **infra-reviewer** | Same subject-area assignment spec 187 used. Docker, the Aspire AppHost and CI are its stated territory, and "does a green run actually prove anything" is exactly the question this diff turns on |

**backend-reviewer: skipped.** No C# production code, no domain, no handler, no
EF, no `Result`/`Option` surface. The only C# in the diff is two test classes.

**security-reviewer: skipped, deliberately, and here is the argument** — because
a supply-chain diff superficially reads as security-sensitive. The change
*narrows* trust in every direction: three floating upstreams become digests, an
unverified download becomes a verified one, a package-supplied identity for the
OIDC broker becomes an explicit one. There is no new trust boundary, no new
secret, no new scope, no new token path, no authorization decision and no
network-reachable surface. What a security reviewer would want to check is the
**values** — and those are corroborated in spec §6 against an independent
packager's record, which is a stronger check than a reading of the diff. If
phase 6 disagrees, adding the reviewer costs one dispatch.

---

## 7. Dependencies, parallelism and contention

**Foundational: nothing.** No `Shared.Kernel`, no `Shared.Contracts`, no Aspire
resource added, no AppHost wiring the rest depends on. Nothing in this feature
blocks anything outside it.

**Ordering inside the feature is real and matters:**

- T004 (observe CF1 red) **must** precede T005 (pin the Dockerfile). Reversed,
  the guard arrives green and phase 4a has failed — the one thing ADR-0144 calls
  a failure rather than a shortcut.
- T002/T008 (baselines) **must** precede everything that edits a judged file.
- US1 and US2 touch disjoint files and are `[P]` **between stories**, but the
  baseline capture (T002) reads tests that US2 later re-runs, so the baseline is
  taken once, first, for both.

**File contention: clear.** The only open PR is
[#2474](https://github.com/smartsolutionslab/smart-sentinel-eye/pull/2474)
(`2300-pin-the-sdk-for-real`); `gh pr view 2474 --json files` lists nothing under
`src/AppHost/`, nothing under `tests/`, and no Dockerfile — it touches
`global.json` and its guard. Every other remote branch was checked for the four
files in §1 and none is in flight. No `[P]` marker in this feature collides with
work outside it.

---

## 8. Risks specific to implementation

| # | Risk | Mitigation |
|---|---|---|
| I1 | The guard folds line continuations wrongly and reports a false red on a Dockerfile that *is* checksummed | §2.3 specifies folding as part of the design, and CF1's expected failure count (3 `FROM`, 1 fetch) is written down — a different count is a scan defect, not a code defect |
| I2 | The digest literal is transcribed with a typo | `docker build` in phase 5 fails immediately with `manifest unknown`; the digests are also re-resolved in T003 rather than copied from the spec |
| I3 | `WithImage` placed after `WithImageTag`, silently resetting the tag to `latest` | `AppHostContainerImagePinTests` F1/F2 catch exactly this — that is what spec 187 built them for, and T011 re-runs them |
| I4 | A restored file keeps its old timestamp and MSBuild skips the rebuild, so a reverted counterfactual still looks broken | Touch the file after every revert; called out in T007 and in spec §4.1 step 3 |
| I5 | `docker build` in phase 5 pulls two new base-image manifests and takes several minutes on a cold cache | Expected, not a fault. The build is the only thing that observes the checksum step running |
| I6 | Keycloak's `26.6` moves between this plan and phase 4 | T009 re-resolves and pins to the patch tag whose digest matches; a mismatch with no matching patch tag is **stop and report**, not a judgement call for the implementer |

---

## 9. Constitution and ADR alignment

- **§Testing / ADR-0144.** Two obligations, both discharged explicitly: US1 red
  (observed on the real unpinned tree), US2 characterisation green (captured
  before, unmodified after). Neither is skipped; 4a has no exemption.
- **ADR-0139.** The rule moves from prose nobody reads to a build failure. This
  is the ADR the feature serves.
- **ADR-0103.** No Testcontainers, no new container in any test. The guard reads
  a file; the only `docker build` in the feature is a phase-5 observation run by
  hand.
- **ADR-0036.** Smallest change: four files, no new tool, no CI job, no
  abstraction extracted, `apt-get` pinning filed rather than attempted.
- **§II, value objects / `PrimitiveBoundaryTests`.** Not engaged — no domain
  model is touched.
- **ADR-0109.** `[P]` markers in `tasks.md` are per disjoint file and are listed
  there.
- **Blocked outcomes (ADR-0144):** this feature may not delete, narrow or disable
  `ContainerImagePinTests` or `AppHostContainerImagePinTests`; may not add a
  filename exclusion list to the new guard; may not widen `IsFloating`; and may
  not write or amend an ADR. Each is a blocked outcome, not a judgement call.
