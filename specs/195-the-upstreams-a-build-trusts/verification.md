# Verification 195 — The upstreams a build trusts (#2296)

**Latency: N/A.** Build/CI infrastructure change; no production assembly
changes, no leg of constitution §IV's event→overlay path touched.

## Summary

`src/AppHost/mosquitto/Dockerfile` had three floating base-image tags and
an unchecksummed download from a third-party host; Keycloak's image tag
was implicit (resolved from the `Aspire.Hosting.Keycloak` package default)
rather than explicit like minio's existing pin. A new guard
(`DockerfileUpstreamPinTests`) closes the Dockerfile gap; an explicit
`WithImageTag`/`WithImageRegistry` chain closes the Keycloak gap.

## Phase 4a — real, naturally-occurring red (ADR-0139)

Not a counterfactual — the guard was written against the tree as it
actually stood, and failed for real:

```
Every_base_image_a_dockerfile_builds_from_is_pinned_by_digest [FAIL]
  3 base image reference(s) in this repository's Dockerfile(s) have no digest:
  src/AppHost/mosquitto/Dockerfile:17 → debian:bookworm-slim
  src/AppHost/mosquitto/Dockerfile:31 → golang:1.23-bookworm
  src/AppHost/mosquitto/Dockerfile:43 → debian:bookworm-slim

Every_archive_a_dockerfile_downloads_is_verified_against_a_checksum [FAIL]
  1 download(s) ... never checked against a checksum:
  src/AppHost/mosquitto/Dockerfile:23 downloads https://mosquitto.org/files/source/mosquitto-${MOSQUITTO_VERSION}.tar.gz

Two_stages_that_build_from_one_image_name_one_digest — Passed
The_scan_reads_the_dockerfiles_this_repository_actually_has — Passed
Total tests: 4  Passed: 2  Failed: 2
```

Independently re-confirmed by the orchestrator (2/4, matching exactly) and
by the phase-6 reviewer before any pin landed.

**Two scan-precision defects found and fixed by test-writer while proving
the red was genuine, not manufactured**: the file-population glob also
matched the guard's own source file (name starts with "Dockerfile"), and
the fetch-detection regex matched `apt-get install ... wget` (installing
the tool) as if it were an invocation. Both fixed before the red was
trusted as real.

## Digests and checksum — independently sourced, re-verified twice

| Upstream | Value |
|---|---|
| `debian:bookworm-slim` | `sha256:3783cc01769c7b2b1b83a5c5ad96c815348e28ed7da68e2e3687004faa906251` |
| `golang:1.23-bookworm` | `sha256:167053a2bb901972bf2c1611f8f52c44d5fe7e762e5cab213708d82c421614db` |
| `mosquitto-2.0.18.tar.gz` | `sha256:d665fe7d0032881b1371a47f34169ee4edab67903b2cd2b4c083822823f4448a` |
| `quay.io/keycloak/keycloak:26.6` and `:26.6.4` | `sha256:0aae0de7fca85525f727d3354df17896092de8bb26ae4c12d89c77e5df8cbce4` (identical for both tags) |

The mosquitto tarball checksum was corroborated against Alpine aports'
independently recorded SHA-512 for the same file (byte-for-byte match) —
a hash computed only from one's own download proves what arrived, not
that what arrived is genuine; the independent cross-check is what closes
that gap. The GPG signature (`.asc`) was confirmed made by the real
mosquitto release key (RSA fingerprint `A0D6EEA1DCAE49A635A3B2F0779B22DFB3E717B7`)
but the key itself couldn't be fetched from any of three keyservers
tried — recorded as a partial verification, not silently treated as full.

All four values were **re-resolved a second time by phase 4b** immediately
before applying the pins (not reused blind from the architect's earlier
research) — no drift found on any of the four.

## The pins — real build evidence, not just a text diff

`docker build` on the pinned Dockerfile succeeded; the log shows
`mosquitto-2.0.18.tar.gz: OK` from `sha256sum -c`. Corrupting
`MOSQUITTO_SHA256` by one byte made the build fail at that exact step
(`WARNING: 1 computed checksum did NOT match` / `FAILED`) — proving the
checksum step is load-bearing, not decorative. Reverted; rebuilt green;
`docker run ... mosquitto -h` reported `mosquitto version 2.0.18`,
confirming the pinned build still produces a working broker.

## F2's counterfactual — proving stage agreement isn't vacuous

`Two_stages_that_build_from_one_image_name_one_digest` passed trivially
before the pin (both `debian:bookworm-slim` occurrences were the same
floating tag) and needed to be shown it discriminates once both carry
real digests. Altering one byte of the runtime-stage digest in a
throwaway edit made F2 fail, naming both lines and both (now different)
digests, while F1 (the "has a digest at all" check) stayed green.
Reverted; `git hash-object` confirmed byte-identical (`bda5c0fc...`) to
the real pinned state before and after.

## Keycloak — before/after probe, reverted both times

Before the pin: `keycloak.Reference` = `"quay.io/keycloak/keycloak:26.6"`.
After: `"quay.io/keycloak/keycloak:26.6.4"` — same image, same registry,
only the tag spelling changed, matching the digest-equality proof above
that this changes nothing about what runs today. Both probe edits (the
temporary assertion used to observe the value) were fully reverted; `git
diff` confirmed clean each time before the permanent identity fact
(`The_keycloak_image_resolves_to_the_upstream_repository_this_stack_expects`,
which never asserts the tag value itself) was committed in its place.

## Independent re-verification, this pass

```
$ dotnet test tests/Architecture.Tests --filter "DockerfileUpstreamPinTests|ContainerImagePinTests"
Passed!  - Failed: 0, Passed: 7, Total: 7

$ dotnet test tests/Integration.Tests --filter "AppHostContainerImagePinTests|AppHostMediaMtxImageTests"
Passed!  - Failed: 0, Passed: 7, Total: 7

$ dotnet build tests/Architecture.Tests/SmartSentinelEye.Architecture.Tests.csproj -c Release
Build succeeded. 0 Warning(s) 0 Error(s)

$ dotnet build src/AppHost/SmartSentinelEye.AppHost.csproj -c Release
Build succeeded. 0 Warning(s) 0 Error(s)

$ dotnet build tests/Integration.Tests/SmartSentinelEye.Integration.Tests.csproj -c Release
Build succeeded. 0 Warning(s) 0 Error(s)
```

Full `Architecture.Tests` project (437 total) and the `Integration.Tests`
`FixtureLogic` category (155 total) both confirmed by phase 4b with no
regressions beyond the intended new/changed facts.

## What was deliberately NOT done, and why — both filed

- **`apt-get install` packages remain unpinned** (`build-essential wget
  ca-certificates libssl-dev libcjson-dev` in the builder stage;
  `libssl3 libcjson1 ca-certificates` in the runtime stage). Pinning with
  `apt-get install pkg=version` breaks the build on a schedule — a Debian
  suite's archive carries only the current version of each package, so
  the pin breaks the moment `bookworm` takes a point release. The only
  real fix (`snapshot.debian.org`) trades build reproducibility for
  frozen security updates on a TLS-terminating broker — a real trade-off
  wider than this issue's stated intent. **Filed as
  [#2475](https://github.com/smartsolutionslab/smart-sentinel-eye/issues/2475).**
  The Dockerfile is pinned, not reproducible, and says so in a comment.
- **Floating-within-minor is unchanged, repository-wide** —
  `rabbitmq:4-management-alpine` and (even after this fix) `26.6.4`
  itself isn't digest-pinned in `AppHost.cs`, only the Dockerfile's own
  base images are. Spec 187 §9 declined to narrow `IsFloating` for the
  same reason this spec declines again: changing it changes multiple
  currently-passing verdicts at once and is a supply-chain decision, not
  a bug fix. **Filed as
  [#2476](https://github.com/smartsolutionslab/smart-sentinel-eye/issues/2476).**
- **The new guard deliberately does not judge `apt-get` lines** — a guard
  for a rule nobody has adopted is a guard someone deletes.
