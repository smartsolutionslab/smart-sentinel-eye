# Tasks 195 — The upstreams a build trusts

**Spec:** `specs/195-the-upstreams-a-build-trusts/spec.md`
**Plan:** `specs/195-the-upstreams-a-build-trusts/plan.md`
**Issue:** [#2296](https://github.com/smartsolutionslab/smart-sentinel-eye/issues/2296)
— **already on Project #13**, status **Todo**, labels `ci`, `tech-debt`,
`agent:ready`, no `agent:blocked`. Confirmed by
`gh project item-list 13 --owner smartsolutionslab --limit 2000` filtered on
`content.number == 2296`, and by `gh issue view 2296` (`projects: Smart Sentinel
Eye (Todo)`). **No `item-add` needed** for this issue — T014's two new issues do
need one each.

---

## Declarations (ADR-0144, required at phase 3)

### Phase 4a colour: **RED** (US1) + **characterisation green** (US2)

**US1 — RED, and the red is free.** `DockerfileUpstreamPinTests` is new
coverage, written **before** the Dockerfile is touched and run against the
Dockerfile as it stands, where it fails for real: three unpinned `FROM`
references and one unverified download. No counterfactual has to be constructed
to produce it — unlike spec 187, whose subject was already correct. A guard that
arrives green here is a **phase-4 failure**, not a shortcut: it would mean the
guard does not read what it claims to read.

Expected red, written down in advance so a different red is recognised as a scan
defect rather than accepted as success:

- **F1 fails**, naming `src/AppHost/mosquitto/Dockerfile` lines **17**, **31**
  and **43**.
- **F3 fails**, naming line **23**.
- **F2 passes** (nothing is pinned, so no two digests can disagree).
- **F4 passes** (1 Dockerfile, 3 `FROM` references, 1 fetch — the scan looked).

**US2 — CHARACTERISATION, observed green.** The covering facts —
`AppHostContainerImagePinTests` (4 facts), `ContainerImagePinTests` (3 facts),
`AppHostMediaMtxImageTests` (2 facts) — are captured green **before** the change
(T002) and must pass **unmodified** after (T011). The new identity fact (T010) is
likewise written and observed green *before* `AppHost.cs` is touched. **An
assertion that has to be edited is evidence the behaviour moved: block, do not
adjust.** No pre-declared exemption — no method is renamed and no message quotes
a name that changes.

### Roles

| Phase | Agent | Note |
|---|---|---|
| 4a | **test-writer** | Owns T001, T002, T004, T006, T008, T010, T012 — writes tests, runs them, returns **verbatim** output including the red. May not edit the Dockerfile or `AppHost.cs` |
| 4b | **infra-engineer** | Owns T003, T005, T007, T009, T011, T013, T014. Dockerfile + Aspire resource + docs. **Not backend-engineer** — no bounded context, no domain, no persistence, no message (plan §6) |
| 6 | **infra-reviewer** only | backend-reviewer skipped (no production C#); security-reviewer skipped with the argument written out in `plan.md` §6 — the diff narrows trust in every direction and adds no boundary, secret, scope or token path |

### Blocked outcomes, restated so they are not rediscovered

- Deleting, narrowing or disabling `ContainerImagePinTests` or
  `AppHostContainerImagePinTests`.
- Adding a filename exclusion list to the new guard (glob the tree; spec 187
  rejected a name list for the same reason).
- Widening `IsFloating` in either existing guard — that would red-line
  `rabbitmq:4-management-alpine` on day one (spec §8.2; file it, do not do it).
- Pinning `apt-get` package versions (spec §8.1; filed by T014).
- Editing a test assertion to make a counterfactual behave.
- Writing or amending an ADR, or the constitution.

---

## Foundational — blocks everything

**Nothing.** No `Shared.Kernel`, no `Shared.Contracts`, no new Aspire resource,
no AppHost wiring anything else depends on. This feature blocks nothing outside
itself and nothing outside it blocks this.

**File contention: clear.** The only open PR is
[#2474](https://github.com/smartsolutionslab/smart-sentinel-eye/pull/2474)
(`2300-pin-the-sdk-for-real`), which touches `global.json` and its guard —
nothing under `src/AppHost/` or `tests/`. Every remote branch was checked for
`src/AppHost/mosquitto/Dockerfile`, `src/AppHost/AppHost.cs`,
`tests/Architecture.Tests/ContainerImagePinTests.cs` and
`tests/Integration.Tests/AppHostContainerImagePinTests.cs`; none is in flight.

---

## US1 — A Dockerfile cannot name an upstream it has not pinned (P1)

**Ships alone and is the issue's headline.**

| ID | P | Story | Task |
|---|---|---|---|
| **T001** | `[P]` | US1 | **Write `tests/Architecture.Tests/DockerfileUpstreamPinTests.cs`.** Structure per `plan.md` §2.1 (public class, no trait, private `RepositoryRoot()` duplicated per the nine that already carry one in this project, `/`-normalised paths). Population per §2.2 — **glob** `Dockerfile*` under the repo root, excluding `node_modules`/`obj`/`bin`, **never a filename constant**. Parsing per §2.3: strip `#` comments **first** (the header at `:3` contains the word FROM), **fold `\`-continuations into logical instructions** remembering the starting physical line, skip a `FROM` whose reference is a stage name declared by an earlier `AS`. Four facts per §2.4, messages per §2.5. **No fact names a digest, a version, an image or a checksum** — ban a category, never a value. Disjoint file: `[P]` against T002. |
| **T002** | `[P]` | US1+US2 | **Capture the baseline.** Run, on the unmodified branch, `ContainerImagePinTests` (3 facts), `AppHostContainerImagePinTests` (4 facts) and `AppHostMediaMtxImageTests` (2 facts); save **verbatim** output. This is US2's characterisation baseline and the proof the tree was sound before anything was added. Disjoint from T001. |
| **T003** | | US1 | **Re-resolve the three Dockerfile values and record what you actually got** (spec §6 holds evidence, not input): `docker buildx imagetools inspect debian:bookworm-slim` and `… golang:1.23-bookworm` → the **index** digests; `curl -o … https://mosquitto.org/files/source/mosquitto-2.0.18.tar.gz && sha256sum`. If a digest differs from spec §6, **use the fresh one and record both** — the registry moved, that is expected. If the **tarball** SHA-256 differs from `d665fe7d…4448a`, **STOP and report**: spec §6 corroborated those exact bytes against Alpine's recorded SHA-512, and a changed release tarball is a supply-chain event, not a refresh. |
| **T004** | | US1 | **Observe the RED (CF1).** With the Dockerfile untouched, run `dotnet test tests/Architecture.Tests --filter FullyQualifiedName~DockerfileUpstreamPinTests`. Capture verbatim. It **must** match the expected red declared above — F1 on lines 17/31/43, F3 on line 23, F2 and F4 green. A different shape (wrong lines, wrong count, a fact passing that should fail) is a **scan defect**: STOP and report, do not adjust the Dockerfile to suit the guard. |
| **T005** | | US1 | **Pin the Dockerfile** (`plan.md` §3.1): `ARG MOSQUITTO_SHA256=<T003 value>` beside `ARG MOSQUITTO_VERSION`; `ARG MOSQUITTO_SHA256` re-declared in the builder stage; `&& echo "${MOSQUITTO_SHA256}  mosquitto-${MOSQUITTO_VERSION}.tar.gz" \| sha256sum -c -` inserted between the `wget` and the `tar` **in the same RUN**; all three `FROM` lines rewritten as `image:tag@sha256:<digest>` — **keep the tag**, and the two `debian` lines carry the **same** digest. Add the comment from §3.2 (pinned, not reproducible; the apt archive is deliberately not frozen; names T014's follow-up). Do not disturb the three existing *why* blocks. Re-run the guard → **all four green**. |
| **T006** | | US1 | **Counterfactual CF2 — prove F2 can fail.** Alter one character of the runtime stage's `debian` digest. Run the guard: **F2 must FAIL** naming both lines and both digests; **F1 must still PASS** (both are still digest-shaped). Capture verbatim. Revert, **`touch` the file** before re-running — a restored file keeps its old timestamp and MSBuild skips the rebuild, which reads exactly like the fix not working — and re-prove green. If F2 passes on the broken tree, it checks nothing: **STOP and report**. |
| **T007** | | US1 | **Build it, and prove the checksum line is live (CF3).** `docker build -t sse-mosquitto-195 src/AppHost/mosquitto` → succeeds; the log shows `mosquitto-2.0.18.tar.gz: OK`. Then alter one character of `MOSQUITTO_SHA256` and rebuild → **the build fails** at that step, naming the computed and expected hashes. Revert, `touch`, rebuild green. Finally `docker run --rm sse-mosquitto-195 /usr/local/sbin/mosquitto -h \| head -1` → `mosquitto version 2.0.18`. Capture all four outputs verbatim; this is US1's **phase-5 evidence** and the only thing that observes the new failure mode existing at all. |

**US1 exit:** the new guard was observed red on the real unpinned tree and is
green on the pinned one; F2 was observed failing on a constructed break; the
image builds, the checksum step runs, and a corrupted checksum stops the build.

---

## US2 — The Keycloak image is named where it is used (P2)

**Ships alone; independent of US1's files.**

| ID | P | Story | Task |
|---|---|---|---|
| **T008** | | US2 | **Probe, before.** In `AppHostContainerImagePinTests`, temporarily assert keycloak's resolved reference equals `"probe"` so the failure message prints what it really is. Run; **record the printed reference verbatim** (expected `quay.io/keycloak/keycloak:26.6`). Revert the probe; confirm `git diff` on that file is empty. This is the only mechanism that makes a passing run *say* what it saw. |
| **T009** | | US2 | **Re-resolve Keycloak and decide the literal.** `docker buildx imagetools inspect quay.io/keycloak/keycloak:26.6` → digest D. Find the **patch tag whose digest equals D** (`26.6.4` on 2026-09-20; check `26.6.5`, `26.6.4` downward via `https://quay.io/api/v1/repository/keycloak/keycloak/tag/?filter_tag_name=like:26.6`). Re-read the package literal (`Aspire.Hosting.Keycloak` 13.5.3-preview.1.26425.3, UTF-16 strings in `lib/net8.0/*.dll`) and confirm it is still `26.6`. **STOP and report** if: no patch tag matches D, or the package literal is no longer `26.6`, or `Directory.Packages.props:34` no longer pins that exact version. The premise moved and the decision is the architect's. |
| **T010** | | US2 | **Write the identity fact, observe it green first.** Add `The_keycloak_image_resolves_to_the_upstream_repository_this_stack_expects` to `AppHostContainerImagePinTests`: keycloak's `ContainerImageAnnotation` has image `keycloak/keycloak`, registry `quay.io`, and a tag that is present and **not floating**. **It must not name the tag** — naming it would make a Keycloak bump edit its own guard. Run it on the **unmodified** `AppHost.cs`: it must be **GREEN** (the package supplies exactly those coordinates today). A red here means the measurement in spec §1.3 was wrong: STOP and report. |
| **T011** | | US2 | **Pin Keycloak** (`plan.md` §4.1): insert `.WithImage("keycloak/keycloak")`, `.WithImageTag("<T009 tag>")`, `.WithImageRegistry("quay.io")` **in that order**, above the existing `.WithRealmImport(...)` which stays last. Add the comment in minio's register (`AppHost.cs:288-296`): the package supplied all three coordinates and this file named none; Keycloak decides realm import, scope issuance and `iss`; `WithImage` resets the tag when given none, so the order is fixed; and the digest T009 recorded, so the next reader can check the claim rather than trust it. Then re-run `AppHostContainerImagePinTests` (5 facts now), `ContainerImagePinTests` (3) and `AppHostMediaMtxImageTests` (2) with **assertions unmodified** → green. |
| **T012** | | US2 | **Probe, after — the characterisation's actual evidence.** Re-run T008's probe against the pinned `AppHost.cs`. **The printed reference must be identical to T008's**, allowing only the tag spelling T009 chose, whose digest equality was proven in T009. If the two differ in image or registry, or the tag resolves to a different digest, the pin **changed what runs**: STOP and report — do not adjust the tag to make them match. Revert the probe; confirm `git diff` shows only the intended fact and the pin. |

**US2 exit:** `AppHost.cs` names image, tag and registry; the composed reference
resolves to the same manifest it did before, observed twice and recorded; seven
pre-existing facts plus one new one are green with every assertion unmodified.

---

## Documentation and follow-ups

| ID | P | Story | Task |
|---|---|---|---|
| **T013** | | both | **Correct the three guard doc comments — not optional** (spec §1.2, §7.1). (a) `AppHostContainerImagePinTests`: the sentence *"its Dockerfile's own base images … are a separate, currently unguarded surface — no guard in this repository reads a Dockerfile"* becomes **false** when T005 lands; rewrite it to name `DockerfileUpstreamPinTests` as that surface's authority. Leaving it is the clerical drift §IV, §II and the Phase-3 board gate were each corrected for. (b) `ContainerImagePinTests`: add one sentence naming the new guard and keeping its own authority ("what is **written** in `AppHost.cs` and `scripts/`"). (c) `DockerfileUpstreamPinTests`: name the other two and say what each is the authority for. Three classes carry `Pin` in the name; without this task the split in spec §7.1 makes the codebase worse. |
| **T014** | `[P]` | — | **File the two deferred decisions as issues, and put them on Project #13.** (a) *`apt-get` package versions in the Mosquitto Dockerfile are not pinned* — spec §8.1's reasoning in full: a Debian suite carries only the current version, so `pkg=version` fails on the next point release, and the only fix is `snapshot.debian.org`, which puts a third-party archive in the build path and freezes security updates for a TLS-terminating broker. (b) *Floating-within-major/minor tags, repository-wide* — `rabbitmq:4-management-alpine` and friends; changing `IsFloating` changes five verdicts and is a supply-chain decision (spec §8.2, spec 187 §9). Both: `gh project item-add 13 --owner smartsolutionslab --url <issue-url>` (needs the `project` scope; `item-add` prints nothing on success — verify with `--limit 2000`). Disjoint from every file above. |

---

## Dependencies

```
T001 ─┐
T002 ─┴─► T004 ──► T005 ──► T006 ──► T007          (US1)
       T003 ──────► T005
       T002 ──► T008 ──► T010 ──► T011 ──► T012     (US2)
              T009 ──────────────► T011
       T005, T011 ──► T013
       (T014 independent)
```

- **T004 before T005 is the phase-4a gate itself.** Reversed, the guard arrives
  green and the obligation is unmet.
- **T002 before anything that edits a judged file** — a baseline taken after the
  change is not a baseline.
- **T008 before T011, T012 after** — one probe on each side of the pin is the
  whole of US2's evidence.
- **T013 after both pins land**, because it describes the state they create.

## Parallel markers (ADR-0109)

`[P]` where files are disjoint: **T001** (new test file) against **T002**
(read-only runs); **T014** (GitHub only) against everything. Everything else is
sequential by the dependency chain above, not by file contention — US1 and US2
touch four disjoint files but share the phase-4a ordering discipline, so they
are listed in order rather than raced.

## What the PR body must quote

1. T004's verbatim **red** — the three `FROM` lines and the fetch.
2. T006's verbatim CF2 red, and F1 still green beside it.
3. T007's build log line `mosquitto-2.0.18.tar.gz: OK`, and the failure when the
   checksum is altered.
4. T002's baseline green and T011's post-change green, side by side, showing the
   assertions unmodified.
5. T008's and T012's probe output, the two reference strings, unedited.
6. The four values from T003/T009 as actually resolved, with the Alpine SHA-512
   corroboration and the note that the GPG signature could **not** be verified
   from this machine (spec §6, R7).
