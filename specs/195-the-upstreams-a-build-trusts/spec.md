# Spec 195 — The upstreams a build trusts

**Issue:** [#2296](https://github.com/smartsolutionslab/smart-sentinel-eye/issues/2296)
— *"The Mosquitto Dockerfile pins nothing and sits outside the image-pin
guard."* Labels `ci`, `tech-debt`, `agent:ready`, no `agent:blocked`.
**Already on Project #13**, status **Todo** — confirmed by
`gh project item-list 13 --owner smartsolutionslab --limit 2000` filtered on
`content.number == 2296` (the number filter returns zero; the board must be
queried by `content.url`/`content.number`). **No `item-add` is needed.**

**Spec number.** `git ls-tree -d` over `origin/develop` **and every remote
branch** shows **194** as the highest `specs/NNN-*` directory anywhere. The only
open PR is [#2474](https://github.com/smartsolutionslab/smart-sentinel-eye/pull/2474)
(`2300-pin-the-sdk-for-real`), which carries no `specs/` directory at all. This
spec is **195**. Re-check after any parked PR merges — two unmerged branches can
both claim a number, and this repository has been bitten by exactly that.

**Branch:** `2296-pin-mosquitto-and-keycloak`, cut from `origin/develop` @
`a61977a0`.

**Created:** 2026-09-20. Phases 1–3 of ADR-0037, run in the ADR-0144 autonomous
lane.

**ADRs referenced:**

- **ADR-0139** (a rule belongs in the build, not in a review). The rule "this
  stack does not trust a floating upstream" is wired to the build for everything
  `AppHost.cs` *writes* and everything the model *composes* — and is wired to
  nothing at all for the one surface that is neither: a Dockerfile.
- **ADR-0100** (Mosquitto + the Go JWT/JWKS auth plugin). The ADR that put this
  Dockerfile in the tree and that explains why it builds Mosquitto from source on
  glibc rather than starting from `eclipse-mosquitto` — the reason there are
  *three* base images to pin instead of one.
- **ADR-0103** (integration tests are Aspire-only; no Testcontainers). Why the
  new guard reads a file in `Architecture.Tests` instead of building an image.
- **ADR-0036** (smallest possible change; no speculative generality).
- **ADR-0037** / **ADR-0144** (the phased workflow; the lane, its two phase-4a
  colours, and the blocked outcome of weakening a gate).
- **ADR-0052** / **ADR-0053** (xUnit + Shouldly; sentence-style test names).
- **ADR-0109** (the `[P]` disjoint-file rule).
- **Spec 113** (`specs/113-the-image-that-defines-a-contract/`) — introduced the
  pin rule, chose version tags over digests for registry images, and wrote
  `ContainerImagePinTests`.
- **Spec 187** (`specs/187-a-pin-the-model-resolves/`) — delivered this session;
  wrote `AppHostContainerImagePinTests`, excluded `DockerfileBuildAnnotation`
  resources by design, and named this Dockerfile "a separate, currently
  unguarded surface" (§9). **This spec is that follow-up.**

**No new ADR is needed.** Nothing architectural is decided. Spec 113 already
decided *that* upstreams are pinned; ADR-0139 already decided that such a rule
is a build failure; ADR-0100 already decided that this Dockerfile exists and
what it builds. This spec pins four upstreams that were missed and gives the one
file nothing reads a guard that reads it. It does not amend the constitution.

**Latency budget (§IV): N/A.** The diff is one Dockerfile, five lines of
`AppHost.cs`, one new test class and two doc comments. No production assembly
changes behaviour; no leg of `event arrival → overlay rendered` is touched, and
no leg's measurement status moves.

---

## 1. The defect, re-measured on this tree

Everything below was re-read on this worktree (`origin/develop` @ `a61977a0`) on
2026-09-20. The issue was filed on 2026-09-13 and its line numbers have since
drifted; a spec that repeats a filed premise without checking it is the defect
this repository has named most often.

### 1.1 `src/AppHost/mosquitto/Dockerfile` — what is actually there

| Issue says | This tree says | Claim holds? |
|---|---|---|
| `:17`, `:27` — `debian:bookworm-slim` twice | **`:17`** (`AS mosquitto_builder`) and **`:43`** (runtime stage) | **Yes** — two floating `debian:bookworm-slim`; the second line number is stale |
| `:44` — `golang:1.23-bookworm` | **`:31`** (`AS plugin_builder`) | **Yes** — floating; line number stale |
| `:23` — unchecksummed `wget` | **`:23`** — `wget -q "https://mosquitto.org/files/source/mosquitto-${MOSQUITTO_VERSION}.tar.gz" && tar xzf …` | **Yes**, line exact |
| `apt-get install` unpinned | **`:19`** (builder: `build-essential wget ca-certificates libssl-dev libcjson-dev`) and **`:51`** (runtime: `libssl3 libcjson1 ca-certificates`) | **Yes** — and see §8.1, this one is deliberately **not** fixed here |

So the finding stands in full; three of its four line references have moved.
`ARG MOSQUITTO_VERSION=2.0.18` sits at `:12`, above the first `FROM`, and is
re-declared inside the builder stage at `:18`.

### 1.2 What each guard actually reads

- `tests/Architecture.Tests/ContainerImagePinTests.cs` — opens
  **`src/AppHost/AppHost.cs`** and the **`scripts/`** tree as text
  (`ContainerImagePinTests.cs:56-58`), matching `AddContainer(name, image, tag)`,
  `WithImageTag("…")` and a written `bluenviron/mediamtx:<tag>` reference. It
  never opens a Dockerfile. The issue's line reference (`:38-39`) is stale; the
  constants are now at `:56-58`.
- `tests/Integration.Tests/AppHostContainerImagePinTests.cs` (spec 187) — composes
  the real application model and reads each resource's
  `ContainerImageAnnotation`. It **deliberately excludes** every resource carrying
  a `DockerfileBuildAnnotation`, and `mosquitto` is the only such resource
  (`AppHost.cs:247`, `.AddDockerfile("mosquitto", "mosquitto")`). Its own doc
  comment says why, and says what is left over:

  > *"Its Dockerfile's own base images (`src/AppHost/mosquitto/Dockerfile`) are a
  > separate, currently unguarded surface — no guard in this repository reads a
  > Dockerfile (spec 187 §9)."*

  That sentence is true today and becomes **false** the moment US1 lands. Making
  it false without editing it is the clerical drift §IV, §II and the Phase-3
  board gate have each been corrected for; **T013 edits it**, and it is not
  optional.

**There is exactly one Dockerfile in this repository** (`find . -name
"Dockerfile*"`, excluding `node_modules`, `obj`, `bin`). The new guard still
enumerates by **glob**, never by that one name — an annotation check admits the
next resource automatically and a name list narrows silently; spec 187 chose the
same way for the same reason.

### 1.3 Keycloak — where its image coordinates come from

`AppHost.cs:127-129`:

```csharp
var keycloak = builder
    .AddKeycloak("keycloak", adminPassword: keycloakPassword)
    .WithRealmImport("../AppHost/Realms");
```

No `WithImage`, no `WithImageTag`, no `WithImageRegistry`. All three coordinates
come from `Aspire.Hosting.Keycloak`, pinned in `Directory.Packages.props:34` at
**`13.5.3-preview.1.26425.3`** (an exact version, no range, no wildcard).

Reading the package's own assembly —
`~/.nuget/packages/aspire.hosting.keycloak/13.5.3-preview.1.26425.3/lib/net8.0/Aspire.Hosting.Keycloak.dll`,
decoded as UTF-16 out of the user-string heap — yields exactly three image-shaped
literals: **`quay.io`**, **`keycloak/keycloak`**, **`26.6`**.

**This resolves deterministically.** A NuGet package version is immutable and
the version is exact-pinned, so every machine that restores gets that same
assembly and therefore that same tag. The drift channel is a **package bump** —
`13.5.3-preview.1.26425.3` → anything — which would move the tag with no diff in
`AppHost.cs`, which is precisely how the issue frames the risk (realm-import
semantics, scope issuance, the `iss` claim; the failure arriving as an opaque
`invalid_scope` or a blanket 401).

`26.6` is **not** floating by either guard's definition (no hyphen-separated
segment equals `latest`), so `AppHostContainerImagePinTests` passes on keycloak
today and would keep passing through a package bump to any non-`latest` tag.
Being pinned-enough for the guard is not the same as being named here.

---

## 2. What this buys, and what it does not

Stated up front because "pinned" is about to appear in a Dockerfile and a reader
will otherwise hear "reproducible", which this build is not and will not be.

**Bought.** Four upstreams that can change the built image with no diff and no
PR stop being able to: two Debian base layers, one Go toolchain, one source
tarball. The failure mode they produce is the one #2265 demonstrated for MinIO —
a red `integration`/`e2e` run on a branch that changed nothing, on whichever
machine pulled first.

**Not bought.** `apt-get update && apt-get install` still resolves against
whatever `deb.debian.org` serves at build time. A digest-pinned base image
freezes the *layers*, not the *archive*: `libssl3` can move under a frozen
`debian:bookworm-slim@sha256:…` on the next point release. §8.1 says why
pinning that is a larger and riskier change than this issue's intent, and it is
filed rather than done. **The Dockerfile after this spec is pinned, not
reproducible**, and the Dockerfile will say so in a comment.

---

## 3. User stories

Two stories, **split by phase-4a colour** (ADR-0144), each shipping alone.

### US1 (P1) — A Dockerfile cannot name an upstream it has not pinned

*Colour: **RED**.* New coverage. The guard is written against the **unpinned**
tree and is therefore observed genuinely failing, for free, with no
counterfactual construction needed. Spec 187's US1 had to manufacture its red;
this one is simply true.

**As** whoever is on call for a red CI run,
**I want** the build to refuse a Dockerfile that trusts a floating tag or an
unverified download,
**so that** the four upstreams this image is built from cannot change what runs
without a diff someone reviewed.

#### Acceptance scenarios (Gherkin)

The four required shapes map onto a build guard like this: **happy** = the
pinned tree passes; **bad request** = a malformed or absent pin is refused;
**conflict** = two stages of one image disagree; **auth** = the trust boundary,
which for a Dockerfile is the byte stream it downloads from a host nobody here
controls.

```gherkin
Scenario: Happy — the pinned tree passes
  Given src/AppHost/mosquitto/Dockerfile names every base image by digest
    And the Mosquitto tarball is verified against a recorded SHA-256
  When DockerfileUpstreamPinTests runs
  Then every fact passes
    And no fact names a digest, a version or a checksum value

Scenario: Bad request — a floating FROM is refused
  Given a FROM line names "debian:bookworm-slim" with no @sha256: digest
  When DockerfileUpstreamPinTests runs
  Then the fact fails
    And the message names the file, the line number and the reference
    And the message explains that a floating tag changes what is built with no diff and no PR

Scenario: Conflict — two stages of one image disagree
  Given the builder stage and the runtime stage both name "debian:bookworm-slim"
    And they name two different digests
  When DockerfileUpstreamPinTests runs
  Then the fact fails naming both lines and both digests
    And the message says the runtime stage must carry the glibc the broker was linked against

Scenario: Auth (the trust boundary) — an unverified download is refused
  Given a RUN instruction fetches an archive with wget or curl
    And that same RUN does not verify it with sha256sum -c
  When DockerfileUpstreamPinTests runs
  Then the fact fails naming the line
    And the message says the build trusts mosquitto.org's bytes with nothing to compare them to

Scenario: The scan proves it looked
  Given the repository contains at least one Dockerfile
  When DockerfileUpstreamPinTests enumerates them
  Then the enumeration is non-empty
    And at least three FROM references and at least one remote fetch were judged
    And an empty enumeration fails with "the scan is broken, not the code"

Scenario: The counterfactual (Phase 5, observed not asserted)
  Given the recorded SHA-256 in the Dockerfile is altered by one character
  When the mosquitto image is built
  Then the build fails at the sha256sum step
    And the failure names the computed and the expected hash
```

**Independent end-to-end test procedure (US1)** — see §4.1.

### US2 (P2) — The Keycloak image is named where it is used

*Colour: **CHARACTERISATION, observed green**.* Behaviour-preserving: the
composed reference before and after is the same manifest, proven by digest, so
nothing about what runs changes today.

**As** whoever reads `AppHost.cs` to find out what Keycloak this stack runs,
**I want** the image, tag and registry written in the file that composes it,
**so that** a `Directory.Packages.props` bump cannot move the broker of realm
import, scope issuance and `iss` without a diff in the file that depends on them.

#### Acceptance scenarios (Gherkin)

```gherkin
Scenario: Happy — the coordinates are named and resolve to the same manifest
  Given AppHost.cs names keycloak's image, tag and registry explicitly
  When the application model is composed
  Then keycloak's ContainerImageAnnotation resolves to quay.io/keycloak/keycloak
    And its tag is the tag recorded in this spec
    And that reference resolves to the digest the package's default resolved to

Scenario: Conflict — WithImage after WithImageTag
  Given WithImage("keycloak/keycloak") is moved below WithImageTag(...)
  When AppHostContainerImagePinTests runs
  Then it fails naming keycloak and a reference ending ":latest"
    And Architecture.Tests.ContainerImagePinTests still passes
    (the order blindness spec 187 was written for, re-proven on a second resource)

Scenario: Bad request — the package moves the repository out from under us
  Given a future Aspire.Hosting.Keycloak supplies a different image or registry
  When the application model is composed
  Then the new fact fails because AppHost.cs and the package disagree
    And the failure names both the expected repository and what resolved

Scenario: Auth — the thing actually at risk
  Given Keycloak decides realm import, scope issuance and the iss claim
  When its image changes without a diff in this repository
  Then the observable failure is an opaque invalid_scope or a blanket 401
    And this scenario is the reason the pin is worth its line
```

**Independent end-to-end test procedure (US2)** — see §4.2.

---

## 4. Independent end-to-end test procedure

Written so a reviewer who trusts none of the above can reach the same verdict.

### 4.1 US1

1. `git checkout 2296-pin-mosquitto-and-keycloak`, then `git stash` any
   Dockerfile change so the tree is the **unpinned** one.
2. `dotnet test tests/Architecture.Tests --filter FullyQualifiedName~DockerfileUpstreamPinTests`
   → **red**, naming lines 17, 31 and 43 and the `wget` on 23.
3. Restore the pinned Dockerfile; **touch it** before re-running — a restored
   file keeps its old timestamp and MSBuild will skip the rebuild, which reads
   exactly like the fix not working.
4. Re-run step 2 → **green**.
5. `docker build -t sse-mosquitto-195 src/AppHost/mosquitto` → succeeds; the log
   shows `mosquitto-2.0.18.tar.gz: OK` from `sha256sum -c`.
6. Change one character of the recorded SHA-256; rebuild → **fails** at that
   step with `WARNING: 1 computed checksum did NOT match`. Revert.
7. `docker run --rm sse-mosquitto-195 /usr/local/sbin/mosquitto -h | head -1`
   → reports `mosquitto version 2.0.18`, i.e. the pinned toolchain still
   produces the broker ADR-0100 expects.

### 4.2 US2

1. On the unmodified branch, run `AppHostContainerImagePinTests` and capture the
   output; then run the **probe** (T008): temporarily assert keycloak's
   reference equals `"probe"` so the failure message prints what it really is.
   Record the string verbatim.
2. Apply the `WithImage`/`WithImageTag`/`WithImageRegistry` change.
3. Re-run the probe. **The two recorded strings must be identical.** If they are
   not, the pin changed what runs — **stop and report**; do not adjust the tag to
   make them match.
4. Re-run `AppHostContainerImagePinTests` and `ContainerImagePinTests` with
   their assertions **unmodified** → green.
5. Boot the stack (`aspire run`) once and log in through the management app, or
   run an Identity integration suite: a token is minted, its `iss` is the same
   realm URL as before, and no `invalid_scope` appears. This is the only step
   that observes the thing the pin protects.

---

## 5. Locked tech choices

Nothing is chosen freshly; all of it is inherited.

| Concern | Choice | Source |
|---|---|---|
| Where a Dockerfile guard lives | `tests/Architecture.Tests`, reading the file from disk | ADR-0103; the reason `ContainerImagePinTests` and `IntegrationTestSelectionTests` are there |
| Test framework / naming | xUnit + Shouldly; sentence-style with underscores | ADR-0052, ADR-0053 |
| Repository-root discovery | a private `RepositoryRoot()` walking up to `SmartSentinelEye.slnx` | the convention in every existing source-scanning guard; duplicated, not extracted (ADR-0036) |
| Path reporting | `/` throughout; `Path.GetRelativePath` normalised | the Windows-green/Linux-red trap this repository has hit |
| How registry images are spelled in `AppHost.cs` | `WithImage` → `WithImageTag` → `WithImageRegistry`, in that order | `AppHost.cs:296-301` (minio), and the ordering hazard spec 187 documented |
| How a Dockerfile base image is spelled | `FROM image:tag@sha256:<64 hex>` — tag **and** digest | §7.2 |

---

## 6. The values this spec freezes

All four resolved on 2026-09-20 from this machine. **Every one is re-resolved at
phase 4** (T003, T009) and the implementer records what they actually got; a
value copied forward without re-checking is how a spec becomes a story about a
tree that no longer exists.

| Upstream | Value | How it was obtained | Independently corroborated? |
|---|---|---|---|
| `debian:bookworm-slim` | `sha256:3783cc01769c7b2b1b83a5c5ad96c815348e28ed7da68e2e3687004faa906251` | `docker buildx imagetools inspect debian:bookworm-slim` — an **OCI image index**, so it covers every platform | n/a — it *is* the registry's answer |
| `golang:1.23-bookworm` | `sha256:167053a2bb901972bf2c1611f8f52c44d5fe7e762e5cab213708d82c421614db` | same command; index digest | the image reports `go version go1.23.12 linux/amd64`, which satisfies `plugin/go.mod`'s `go 1.23` |
| `mosquitto-2.0.18.tar.gz` | `sha256:d665fe7d0032881b1371a47f34169ee4edab67903b2cd2b4c083822823f4448a` | downloaded from `https://mosquitto.org/files/source/` and hashed locally (796 351 bytes) | **yes, and this is the important one** — see below |
| `quay.io/keycloak/keycloak` | tag **`26.6.4`**, digest `sha256:0aae0de7fca85525f727d3354df17896092de8bb26ae4c12d89c77e5df8cbce4` | `docker buildx imagetools inspect` on `26.6`, `26.6.4` and `26.6.4-1` — **all three are one manifest today** | the image already on this machine (pulled ~2 months ago) carries that same digest |

**Why the tarball hash can be trusted.** A hash computed from a download proves
only what arrived, which is worth very little on its own — it would faithfully
record a compromised byte stream. Two independent facts back it:

1. **Alpine's `aports` recorded the same bytes.** `main/mosquitto/APKBUILD` on
   the `3.19-stable` branch, at `pkgver=2.0.18`, carries
   `sha512sums="63f7e2811964bab5856848e6918627c47afc6534ff60aad5ece3d2fa330b407c9df14027610826e343ee68ff7d8d5d93f2459713061251ded478c42766946767  mosquitto-2.0.18.tar.gz"`.
   The SHA-512 of the file downloaded here is **that string, exactly**. A
   different byte stream matching a recorded SHA-512 is not a thing that happens.
2. **The upstream detached signature is by the expected key.**
   `mosquitto-2.0.18.tar.gz.asc` is present (HTTP 200) and `gpg --verify` reports
   `Signature made Mon Sep 18 23:29:34 2023, using RSA key
   A0D6EEA1DCAE49A635A3B2F0779B22DFB3E717B7` — Roger Light's release key, and the
   `Last-Modified` the server reports for the tarball is the same day.
   **The signature itself could not be checked**: three keyservers
   (`keys.openpgp.org`, `keyserver.ubuntu.com`, `pgp.mit.edu`) all answered
   `No data` for that fingerprint from this machine, so `gpg` ends at
   `Can't check signature: No public key`. Recorded as a **partial** verification,
   not claimed as a full one — fact 1 is what the pin actually rests on.

---

## 7. The two decisions this spec makes

### 7.1 A new guard, not an extension of either existing one

The issue leaves this open ("Extend `ContainerImagePinTests`' file set … — or,
per #2270, make the guard read the composed manifest"). **Neither. A third,
small guard class: `tests/Architecture.Tests/DockerfileUpstreamPinTests.cs`.**

*Why not the composed-model guard (`AppHostContainerImagePinTests`).* It
structurally **cannot** see this. `mosquitto` carries a
`DockerfileBuildAnnotation`; the `ContainerImageAnnotation` Aspire gives it is a
**content-addressed local name**, computed from the build, not parsed from the
Dockerfile. No `FROM` line feeds into it. Judging it against a registry tag would
fail the build over a string no registry ever sees — which is exactly why spec
187 excluded it, by annotation rather than by name. Extending that class here
would mean making a model-reading guard open a file, which is the one thing its
entire doc comment argues it should not do.

*Why not extend `ContainerImagePinTests`' file set.* Its file set is not the
obstacle; its **grammar** is. Every regex in it is C#-shaped
(`AddContainer("…","…","…")`, `WithImageTag("…")`, a written
`bluenviron/mediamtx:<tag>`), and every fact it states is about MediaMTX
consistency or an `AppHost.cs` literal. A Dockerfile needs three unrelated
regexes (`FROM … @sha256:`, multi-stage `AS`/`--from` references, `RUN … wget`
against `sha256sum -c`) and two obligations that class has no notion of — a
download checksum, and two stages of one image agreeing. Adding them would
roughly double a class whose stated subject is "a container image the AppHost
**composes**", and would make its failure messages point at two unrelated
surfaces. Its own doc already draws the line: it "remains the authority for what
is **written**" in `AppHost.cs` and `scripts/`.

*The cost, stated.* Three classes now carry `Pin` in the name, and a reader has
to know which one to look in. That is paid down by **T013**: each of the three
doc comments names the other two and says what each is the authority for.
Without T013 this decision makes the codebase worse, which is why T013 is a task
and not a nicety.

*Rejected third option:* a `hadolint` stage in CI. It would catch `DL3006`
(untagged or unpinned `FROM`) but neither the tarball checksum nor the two-stage
agreement, and it adds a tool, a config file, a version to pin and a CI job to a
repository whose every other convention guard is a `[Fact]` — speculative
generality for one file (ADR-0036).

### 7.2 Keycloak pins to `26.6.4`, spelled the way minio is

**The change:**

```csharp
var keycloak = builder
    .AddKeycloak("keycloak", adminPassword: keycloakPassword)
    .WithImage("keycloak/keycloak")
    .WithImageTag("26.6.4")
    .WithImageRegistry("quay.io")
    .WithRealmImport("../AppHost/Realms");
```

All three coordinates, in that order, for the reason written out at
`AppHost.cs:288-296` for minio: the package supplies all three and this file
named none, so a package bump could move the **image** or the **registry** just
as silently as the tag; and `WithImage` re-parses the reference and resets the
tag to `latest` when given none, so it must precede `WithImageTag`.

**Why `26.6.4` and not `26.6`.** The instruction a pin must obey is *freeze what
runs today, do not guess*. Today `26.6`, `26.6.4` and `26.6.4-1` are **the same
manifest** — `sha256:0aae0de7fca8…` for all three, which is also the image
already on this machine. So `26.6.4` pulls byte-for-byte what the package's
default pulls right now, while additionally closing the channel `26.6` leaves
open: quay publishes `26.6.0` … `26.6.4`, and `26.6` will move to `26.6.5` the
day it ships, carrying exactly the realm-import and scope-issuance changes this
issue is about. `26.6` would close the package-bump channel only; `26.6.4`
closes both at no cost today.

**This is not the repo-wide floating-within-major question.** Spec 187 §9
deliberately declined to widen either guard's `IsFloating` to catch
`rabbitmq:4-management-alpine`, and nothing here widens it: `26.6` would have
passed and `26.6.4` passes. This is a choice about how **one new pin is
spelled**, not about what the guards reject. §8.2 keeps the wider question filed.

**Re-check at phase 4 (T009), because this is the one value that can rot.** Run
`docker buildx imagetools inspect quay.io/keycloak/keycloak:26.6`; pin to the
**patch tag whose digest equals that answer**. If by then `26.6` resolves to
something with no matching patch tag, or the package's baked literal is no longer
`26.6`, **stop and report** — the premise moved and the decision is the
architect's, not the implementer's.

---

## 8. Out of scope, filed not done

### 8.1 `apt-get` version pinning — deferred, deliberately

The packages are `build-essential wget ca-certificates libssl-dev libcjson-dev`
(builder, `:19-20`) and `libssl3 libcjson1 ca-certificates` (runtime, `:51-52`).

Pinning them with `apt-get install pkg=version` **breaks the build on a
schedule**. A Debian suite's archive carries only the *current* version of each
package; the moment `bookworm` takes a point release — or a security update to
`libssl3`, the likeliest of the eight to move — the pinned version is gone from
the mirror and `apt-get install` fails with `Version 'x' for 'libssl3' was not
found`. The only way to make it work is `snapshot.debian.org`, which means
putting a third-party archive host in the build path and freezing security
updates for a broker that terminates TLS.

That is a trade-off with a real argument on each side and a blast radius beyond
this issue's stated intent (three base images, one checksum, one Keycloak tag).
**Filed as a follow-up issue** (T014) with this reasoning, and the Dockerfile
carries a comment saying the archive is deliberately not frozen, so the next
reader does not mistake the omission for an oversight. The new guard **does not**
judge `apt-get` lines — a guard for a rule nobody has adopted is a guard someone
deletes.

### 8.2 Floating-within-minor, repository-wide

`rabbitmq:4-management-alpine` (`AppHost.cs:118`) and, before this spec, the
package's `26.6` are the same shape: pinned enough for `IsFloating`, still free
to move within a major or minor. Spec 187 §9 declined to narrow it; this spec
declines too. **Filed as a follow-up** (T014, second issue) covering all images
at once, because changing `IsFloating` changes five verdicts and is a
supply-chain decision, not a bug fix.

### 8.3 Not touched at all

Mosquitto's version (`2.0.18` stays), the Go plugin and its `go.sum`, the realm
JSON, `mosquitto.conf`, the `chown` entrypoint, and every other container's tag.

---

## 9. Risks

| # | Risk | Handling |
|---|---|---|
| R1 | A digest-pinned `debian:bookworm-slim` stops receiving base-layer security updates until someone bumps it | Accepted and **written in the Dockerfile**: this is the cost of the pin, and the apt archive is unfrozen anyway (§2), so the runtime still picks up `libssl3` updates at build time. Bumping is a one-line diff a human reviews — which is the entire point |
| R2 | The recorded digest is stale by the time phase 4 runs | T003 re-resolves and records what it actually got; the spec's values are evidence, not input |
| R3 | The multi-arch index digest does not cover the CI runner's platform | It is an **OCI image index**, not a single manifest — it covers every platform the tag covered. CI is linux/amd64 and `docker build` selects from the index exactly as it did from the tag |
| R4 | Pinning Keycloak changes what a clean `dotnet restore` machine pulls | Investigated in §1.3 and §6: the package version is exact-pinned and immutable, `26.6` → `26.6.4` → the running digest are one manifest, and T008's before/after probe **observes** the composed reference is unchanged rather than assuming it |
| R5 | The new guard is written to pass rather than to discriminate | It is written against the **unpinned** tree and observed red there (§3, US1). No constructed counterfactual is needed, which is strictly stronger evidence than spec 187 could get |
| R6 | `sha256sum` is absent from the builder image | It is in `coreutils`, present in `debian:bookworm-slim`; step 5 of §4.1 observes it running |
| R7 | The tarball's GPG signature could not be verified (§6) | Recorded as partial, not claimed. The pin rests on Alpine's independently recorded SHA-512 matching byte-for-byte. If a reviewer wants the signature checked, the key must be fetched from a machine with keyserver access — noted, not blocking |
| R8 | Three classes named `*PinTests` confuse the next reader | T013, non-optional: each doc comment names the other two and states its own authority |

---

## 10. Phase-4a colour declaration (ADR-0144)

| Story | Colour | Why |
|---|---|---|
| **US1** | **RED** | New coverage. `DockerfileUpstreamPinTests` is written first and run against the unpinned Dockerfile, where it **fails for real**. The Dockerfile change then makes it green. A guard arriving green here is a phase-4 failure |
| **US2** | **CHARACTERISATION, observed green** | Behaviour-preserving: the composed reference is the same manifest before and after (§6, §7.2). The covering facts — `AppHostContainerImagePinTests` (4) and `ContainerImagePinTests` (3) — are captured green **before** the change and must pass **unmodified** after. An assertion that has to be edited is evidence the behaviour moved: **block, do not adjust**. The new identity fact (T010) is likewise written and observed green *before* the pin |

Ambiguity resolves to red (ADR-0144), and the one genuinely ambiguous element —
adding `sha256sum -c`, which preserves today's outcome while creating a new
failure mode for changed inputs — is therefore handled as
**red-plus-observation**: it lives in US1, and its new failure mode is proven by
the §4.1 step-6 counterfactual build rather than by an assertion.
