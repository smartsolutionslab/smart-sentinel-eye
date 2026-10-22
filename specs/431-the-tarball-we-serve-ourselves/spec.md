# Spec 431 — The tarball we serve ourselves

**Issue:** [#3089](https://github.com/smartsolutionslab/smart-sentinel-eye/issues/3089)
— *Mosquitto source download depends on mosquitto.org.* Labels `tech-debt`,
`agent:ready`; on Project #13 (status In Progress). **No `item-add` needed.**

**Owner decision (issue comment, 2026-10-22):** option 1 — mirror the tarball as
a release asset of this repository and keep the SHA-256 check. **No ADR.** The
asset already exists: release `vendor-mosquitto-2.0.18` holds
`mosquitto-2.0.18.tar.gz`, 796 351 bytes, GitHub-reported digest
`sha256:d665fe7d…4448a` — re-read at phase 1 with `gh release view`, equal to
`MOSQUITTO_SHA256` in `src/AppHost/mosquitto/Dockerfile`.

**Spec number.** 427 is the highest on `origin/develop`; 428–430 are claimed by
open PRs (#2809, #3073, #3076 branches). Sibling worktrees `D:\Github\sse-*`
claim nothing higher. This spec is **431**. Re-check after any parked PR merges.

**Branch:** `ci/3089-mosquitto-source-from-release`, from `origin/develop` @ `6206ecf9`.

**ADRs referenced:** ADR-0100 (why this Dockerfile builds Mosquitto from source,
so there is a tarball at all); ADR-0139 (a rule belongs in the build);
ADR-0103 (guards read files, they do not build images); ADR-0036 (smallest
change); ADR-0037 / ADR-0144 (phases, 4a colour); ADR-0052 / ADR-0053 (xUnit +
Shouldly, sentence-style names). **Spec 195** wrote the guard this extends and is
historical — not edited.

**Latency budget (§IV): N/A.** Build infrastructure only; the image contents are
byte-for-byte unchanged (same tarball, same hash, same build). No leg moves.

---

## 1. Premise, re-verified (phase 1 is an investigation)

| Claim in the issue | Checked | Result |
|---|---|---|
| The Dockerfile fetches from `mosquitto.org` | `src/AppHost/mosquitto/Dockerfile:40` | True: `wget -q "https://mosquitto.org/files/source/mosquitto-${MOSQUITTO_VERSION}.tar.gz"`, then `sha256sum -c -`, then `tar xzf`, one `RUN`, `&&`-chained. |
| A guard reads this Dockerfile | `grep` of `tests/` for Dockerfile / wget / sha256sum | **One class:** `tests/Architecture.Tests/DockerfileUpstreamPinTests.cs` (spec 195). `ContainerImagePinTests` and `Integration.Tests/AppHostContainerImagePinTests` do **not** read it (that class's own summary says so). |
| A guard asserts the `mosquitto.org` URL | `grep -rn "mosquitto.org" tests src docs .github` | **None.** The only hits outside `specs/195-*` are the Dockerfile line itself. No guard needs changing to accept the new host. |
| The checksum guard stays meaningful | `Every_archive_a_dockerfile_downloads_is_verified_against_a_checksum` | Stays meaningful but is **weaker than its name**: it requires `sha256sum -c` *somewhere* in the same logical `RUN`. `wget … && tar xzf … && sha256sum -c -` passes it, and so does `sha256sum -c - ; tar xzf …` (`;` extracts after a failed check). Neither is live; both are reachable by the edit this spec asks for. |
| Docs/ADRs name the URL | `grep` of `docs/` | None. ADR-0100 says "rebuild from source", not where from. Nothing outside the Dockerfile follows. |
| The asset is the same bytes | `gh release view vendor-mosquitto-2.0.18 --json assets` | Digest equals `MOSQUITTO_SHA256`. The build's own `sha256sum -c` re-proves it on every uncached build. |

## 2. User story

### US1 (P1) — A CI build does not depend on mosquitto.org being up

As a maintainer, I want the Mosquitto image's source tarball fetched from a
release of this repository, so that an outage of a third-party host no longer
fails every integration and e2e shard on every open PR (run 39661988192).

**Acceptance (Gherkin):**

```gherkin
Scenario: the tarball comes from this repository's release
  Given src/AppHost/mosquitto/Dockerfile
  When its download instruction is read
  Then the URL starts with https://github.com/smartsolutionslab/smart-sentinel-eye/releases/download/
  And no remote fetch in any Dockerfile names another host

Scenario: the bytes are proven before they are used
  Given the download RUN
  Then `sha256sum -c` appears before `tar` extracts the archive
  And nothing between them lets extraction run after a failed check (no `;`, no `||`)

Scenario: a version bump is one ARG plus one uploaded asset
  Given MOSQUITTO_VERSION=2.0.18
  Then the URL names the release tag and file only through ${MOSQUITTO_VERSION}
  And contains no literal dotted version

Scenario (conflict): the asset and the hash disagree
  Given an asset whose bytes differ from MOSQUITTO_SHA256
  Then the image build fails at `sha256sum -c` and nothing is extracted

Scenario (bad request): a bump that forgot the upload
  Given MOSQUITTO_VERSION bumped with no matching release asset
  Then `wget` fails (404) and the build stops — loudly, before any extraction
```

Auth: N/A — public release asset, anonymous `wget`, same as today's host.

**Independent end-to-end test:** with the Docker stack stopped, `docker build
src/AppHost/mosquitto` (no cache) succeeds; then one Aspire boot reaches
`mosquitto` Running and an MQTT-touching integration class passes (e.g.
`NFR002_MqttConnectAuthTests`). Finally, with `mosquitto.org` made unreachable
(a hosts-file entry to `0.0.0.0`), the no-cache build still succeeds — the
outage the issue reports, reproduced and survived.

## 3. Locked choices

- Option 1 exactly: one `wget` from the release URL. No fallback to mosquitto.org
  (option 3 was offered and not chosen).
- URL shape: `https://github.com/smartsolutionslab/smart-sentinel-eye/releases/download/vendor-mosquitto-${MOSQUITTO_VERSION}/mosquitto-${MOSQUITTO_VERSION}.tar.gz`.
- `MOSQUITTO_SHA256` ARG and the `sha256sum -c` step unchanged.
- The bump procedure lives in the Dockerfile comment next to the ARGs (the place a
  bumper reads). `docs/runbooks/` holds one unrelated file; a new runbook for a
  three-step procedure would be a second place to keep honest (ADR-0036).

## 4. Assumption, marked

**[ASSUMPTION A1]** The new "fetched only from this repository's releases" fact
applies to **every** Dockerfile, not only Mosquitto's — spec 195's class bans
categories, never values. Only one Dockerfile exists, so no live case is
affected; a future Dockerfile needing a second upstream would have to mirror it
or change the guard in a reviewed PR. If the owner wants the rule scoped to this
file, the fact narrows; nothing else changes.

## 5. Out of scope

Mirroring the Go module downloads (`go mod download`) or apt; spec 195 /
issue #2475 already decided apt stays unfrozen. Corroborating against Alpine
aports' sha512 again — the owner did so before upload, and the hash is unchanged.
