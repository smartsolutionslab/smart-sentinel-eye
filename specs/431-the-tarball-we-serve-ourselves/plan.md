# Plan 431 — The tarball we serve ourselves

**Spec:** [spec.md](./spec.md) · **Tasks:** [tasks.md](./tasks.md) · **Issue:** #3089

## 1. Constitution and ADR check

| Authority | How this plan complies |
|---|---|
| ADR-0100 | Dockerfile build unchanged except the host the tarball comes from; same bytes, same stages. |
| ADR-0139 | The owner's decision is made a build failure (fact F5), not a comment. |
| ADR-0103 | Guards read the Dockerfile as text in `Architecture.Tests`; no image build in a test. |
| ADR-0036 | One `wget` line, one comment block, three facts added to the existing class. No new class, no new runbook, no allowlist. |
| ADR-0144 | Phase 4a **red** (§4). No existing assertion is edited or weakened. |
| No ADR | The owner ruled "No ADR" on the issue; this applies spec 195's pin rule to a source host. |
| Constitution §IV | N/A — no runtime code. |

Bounded contexts, `Shared.Contracts`, AppHost C#, migrations, frontend: untouched.
Engineer: **infra-engineer**.

## 2. Files

| File | Change |
|---|---|
| `src/AppHost/mosquitto/Dockerfile` | Line 40 URL → release URL (spec §3). Comment above the ARGs gains: why the host is this repo (mosquitto.org outage 2026-10-22, #3089; the hash proves the bytes, the host only serves them) and how to bump (download upstream tarball, verify it against upstream + Alpine aports' sha512, `gh release create vendor-mosquitto-<v> --prerelease <file>`, update both ARGs). |
| `tests/Architecture.Tests/DockerfileUpstreamPinTests.cs` | Three new facts + their synthetic counterfactual facts (§3). Existing seven facts unchanged. |

No shard-filter entry: Architecture.Tests is not sharded, and the facts join an existing class.

## 3. The guard additions (all in `DockerfileUpstreamPinTests`)

New private constant: `ThisRepositoryReleases = "https://github.com/smartsolutionslab/smart-sentinel-eye/releases/download/"`.
`FetchInstruction` gains the logical instruction text (needed for ordering); `ClassifyFetch` stays the single classifier.

| Fact | Asserts | Today (live file) |
|---|---|---|
| **F5** `Every_archive_a_dockerfile_downloads_comes_from_a_release_of_this_repository` | every fetch's extracted URL starts with `ThisRepositoryReleases`; `(no URL found)` fails too (a URL hoisted into a variable cannot be judged, so it is not trusted) | **RED** — names `src/AppHost/mosquitto/Dockerfile:40 downloads https://mosquitto.org/files/source/mosquitto-${MOSQUITTO_VERSION}.tar.gz` |
| **F6** `A_downloaded_archive_is_checked_before_it_is_extracted` | in each fetch's logical instruction, the first `sha256sum\s+-c` index < the first `\btar\s+\S*x` index, and the text between them contains neither `;` nor `\|\|`; plus a floor: ≥1 live fetch has an extraction (so F6 cannot pass vacuously) | green (hardening of F3) |
| **F7** `A_download_url_names_its_version_only_through_a_build_arg` | each fetch URL contains no `\d+\.\d+\.\d+` and contains ≥1 `${NAME}` whose `NAME` is declared by an `ARG` in the same file | green (hardening) |

Counterfactual facts (synthetic `LogicalInstruction`s through `ClassifyFetch`, the
pattern spec 195's phase-6 facts already use), each a separate `[Fact]` so the red
side is permanent, not a one-off observation:

- `A_download_from_another_host_is_rejected` — mosquitto.org URL and an
  `example.com` URL fail F5's predicate; the release URL passes; a lookalike
  `https://github.com/someone-else/…/releases/download/` fails.
- `A_checksum_after_extraction_or_behind_a_semicolon_does_not_count` —
  `wget … && tar xzf … && sha256sum -c -` fails; `… sha256sum -c - ; tar xzf …`
  fails; `… sha256sum -c - || true && tar …` fails; the live shape passes.
- `A_hard_coded_version_in_a_download_url_is_rejected` — `vendor-mosquitto-2.0.18/mosquitto-${MOSQUITTO_VERSION}.tar.gz`
  fails; an undeclared `${OTHER}` fails; the live shape passes.

The predicates are private static functions over a single instruction (plus the
file's declared ARG names for F7), so the live facts and the counterfactual facts
call the same code — an assertion that cannot drift from what it proves.

## 4. Phase 4a colour: RED

Behaviour-changing build infrastructure: the host the build trusts moves. F5 is
observed **red** against the unmodified Dockerfile with the expected message in
§3 — any other red (e.g. a scan finding zero fetches) is a scan defect, not
success. F6/F7 arrive green against the live file *by construction* (they harden
an already-correct shape); their red is proven twice: by the synthetic facts
above (permanent: they assert the predicate rejects each violation), and by **planted violations on the
real file**, run once, output quoted in the PR, then reverted:

1. URL host → `mosquitto.org` (after the fix): F5 red.
2. Swap to `&& tar xzf … && sha256sum -c -`: F6 red, **F3 still green** — proving F6 adds coverage F3 lacks.
3. `vendor-mosquitto-2.0.18` literal in the tag segment: F7 red.

Existing facts F1–F4 + three synthetic facts: captured green before, green
unmodified after.

## 5. Verification (phase 5)

No-cache `docker build src/AppHost/mosquitto` green; same with `mosquitto.org`
blackholed in the hosts file; one Aspire boot with `mosquitto` Running and
`NFR002_MqttConnectAuthTests` green. CI integration + e2e shards green on the PR
are the population-level proof.

## 6. Commits (each builds alone, ADR-0087)

1. `test(3089): a dockerfile download comes from this repository's release, checked before extraction, versioned by ARG (red)` — F5–F7 + counterfactuals; F5 red.
2. `ci(3089): the mosquitto tarball is fetched from this repository's release` — Dockerfile URL + comment; F5 green.
