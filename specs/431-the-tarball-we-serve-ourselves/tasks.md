# Tasks 431 — The tarball we serve ourselves

**Spec:** [spec.md](./spec.md) · **Plan:** [plan.md](./plan.md) · **Issue:** #3089
(on Project #13, In Progress — no `item-add` needed).

## Declarations (ADR-0144, phase 3)

- **Phase 4a colour: RED.** F5 red against the live Dockerfile, expected message
  naming `src/AppHost/mosquitto/Dockerfile:40` and the mosquitto.org URL. F6/F7
  are hardening facts, green on the live file; their red is shown by permanent
  synthetic facts and by three planted violations on the real file (plan §4).
- **Engineer:** infra-engineer. **ADR:** none (owner ruling on the issue).
- **Latency:** N/A.
- **Parallelism:** none — two files, strictly sequential (test before fix). No `[P]`.

## Phase 1 — Characterise (US1)

- [ ] **T001 [US1]** Run `DockerfileUpstreamPinTests` on the unmodified tree; record
  7/7 green verbatim. These facts must stay green **unmodified** to the end.

## Phase 2 — Red (US1)

- [ ] **T002 [US1]** In `tests/Architecture.Tests/DockerfileUpstreamPinTests.cs` add
  `ThisRepositoryReleases`, the three predicates, the instruction text on
  `FetchInstruction`, facts F5/F6/F7 and the three counterfactual facts (plan §3).
  Edit no existing assertion. *Depends on T001.*
- [ ] **T003 [US1]** Run the class; quote verbatim. Expected: F5 red with plan §3's
  message, everything else green. A different red → fix the scan, not the expectation.

## Phase 3 — Green (US1)

- [ ] **T004 [US1]** `src/AppHost/mosquitto/Dockerfile`: release URL (spec §3); keep
  `sha256sum -c` and the `&&` chain; comment gains the why (#3089 outage) and the
  bump steps (verify upstream + aports sha512, `gh release create
  vendor-mosquitto-<v> --prerelease`, update both ARGs). *Depends on T003.*
- [ ] **T005 [US1]** Run the class: 13/13 green (7 existing unmodified + 6 new).
- [ ] **T006 [US1]** Planted violations on the real Dockerfile, one at a time, each
  reverted: (1) host back to mosquitto.org → F5 red; (2) checksum after `tar` →
  F6 red **and F3 green**; (3) literal `2.0.18` in the tag segment → F7 red. Quote
  each output. `git diff` clean afterwards.

## Phase 5 — Verify

- [ ] **T007** No-cache `docker build src/AppHost/mosquitto` green; repeat with
  `mosquitto.org` blackholed in the hosts file (ask the user if editing it needs
  elevation). One Aspire boot: `mosquitto` Running, `NFR002_MqttConnectAuthTests`
  green. Write the verification note.
