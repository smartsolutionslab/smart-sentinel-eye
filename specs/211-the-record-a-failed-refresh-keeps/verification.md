# Verification — Spec 211 (#2432)

## Phase 4a — RED, independently re-confirmed

Re-ran `pnpm --filter management-web exec vitest run src/features/cameras/CameraDetailPage.test.tsx --reporter=verbose` myself against the pre-fix tree (commit `ec190f20`, tests only): the three cases the test-writer reported red were independently reproduced red — `Keeps showing the camera when its own refresh fails`, `Retries the refresh when the operator presses Retry`, `Keeps the retired camera behaviour when its refresh fails` — and the three designed to already be green (`Shows no such camera when the failed identifier has no record of its own`, `Says nothing about access while showing a refresh failure`, `Shows no alert when a refresh succeeds`) were green, matching tasks.md's predicted colour column exactly.

## Phase 4b — GREEN, independently re-confirmed

Re-ran the same command against the post-fix tree (commit `a0fe9857`): **19/19 green**, including the three previously-red cases, with zero edits to any existing test or assertion (`git diff --stat` on the two test files shows only additions, confirmed against the test-writer's own commit `ec190f20`). Also independently ran:
- `pnpm typecheck` (all three frontend apps + `typecheck:e2e`) — clean.
- `pnpm lint` (all three apps + `lint:e2e`) — clean, `--max-warnings 0`.

## Phase 5 — live end-to-end procedure: attempted, not completed

`spec.md`'s "Independent end-to-end test procedure" and `tasks.md`'s T002 (the Playwright case in `e2e/camera-detail.spec.ts`) both require a live Aspire stack. This was attempted and **could not be completed on this delivery pass**, for a reason worth recording rather than omitting.

**What happened:** `dotnet run --project src/AppHost` (with `ASPIRE_ALLOW_UNSECURED_TRANSPORT=true` after an initial failed attempt without it — the `http` launch profile does not set this, and Aspire refuses to start without it or an `https` binding) booted successfully — dashboard reachable, `management-web` and `keycloak` reported `Running`, most backend services were still `Waiting` on their dependency chain. During this boot, **free system RAM fell from 6.7 GB to 1.9 GB** (of 23.8 GB total) inside about two minutes — the same OOM-risk pattern already recorded from this session's work on #2284 and #2431. Rather than push further and risk an uncontrolled kill of the AppHost, its containers, or unrelated processes on a shared machine, the stack was torn down deliberately: the AppHost process stopped, its 9 Docker containers (`mosquitto`, `keycloak`, `mediamtx`, `pgadmin`, `fixture-video`, `rabbitmq`, `minio`, `camera-sim`, `postgres`) stopped explicitly (stopping the AppHost alone does not stop containers it orchestrated — they outlive it), and `dotnet build-server shutdown` run to release idle MSBuild/compiler-server node-reuse processes left over from the build. RAM recovered to 5.8 GB free afterward; disk (13 GB free on `C:` throughout, unrelated to this) never became a factor.

**What this means for the gate:** phase 4 (both colours, independently reproduced), typecheck and lint are complete and green. The live end-to-end procedure in `spec.md` and the Playwright case (T002) are written, and T002 typechecks, but **neither has been executed against a running stack**. Per `tasks.md`'s own stated fallback, T001's red was sufficient to unblock T003 (the implementation) — it was. T002's execution was stated as required before T006 (the PR); this record is the explicit, honest acknowledgment that it has not happened yet, rather than a fabricated pass.

**Recommendation, not a blocker by fiat:** run `e2e/camera-detail.spec.ts`'s new case (or the manual procedure in spec.md) on a machine/session with more headroom before merge, or after this machine's memory pressure clears (Rider's own ReSharper host process was the largest single consumer observed and was correctly left untouched, per this repo's standing practice). The change itself is small, single-file, and its correctness rests on a mechanism (`currentData` vs `data`) verified directly against the pinned `@reduxjs/toolkit@2.12.0` source in `plan.md`/`spec.md` — not on the live run alone — so the unit-level proof is not a placeholder for the live one, but the live one is still owed before this is called fully verified.

Latency: **N/A** — the management console is off constitution §IV's six legs (no event→overlay leg touched).
