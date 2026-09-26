# Tasks 253: The bits the decoder throws away

**Spec**: [spec.md](spec.md) · **Plan**: [plan.md](plan.md) · **Issue**: #2533
**Phase 4a colour**: **red**. The "last char `A`" cases must be observed failing against the current
helper, and that output is quoted verbatim in the PR.
**Parallelism**: none. There are four sequential tasks on two files, with no foundational blocker
elsewhere.

## US1 (P1): a corrupted refresh token is always corrupt

- [ ] **T001 [US1] (4a, `test-writer`)** In
  `tests/Integration.Tests/Identity/LockoutSessionSurvivalIntegrationTests.cs`, change `CorruptSignature`
  from `private static` to `internal static`. Change **only** the modifier, never the body. Create
  `tests/Integration.Tests/Identity/CorruptSignatureTests.cs`: a fixture-free class with no
  `[Collection]`, xUnit + Shouldly, sentence-style names. Cover the spec §4 scenarios:
  - 64-byte signatures ending `A`, `Q`, `g`, `w`
  - 32- and 256-byte signatures ending `A`
  - header and payload returned byte-identical
  - the non-three-segment input still fails

  Compare **decoded bytes** (`System.Buffers.Text.Base64Url`), never strings. Run
  `dotnet test tests/Integration.Tests --filter "FullyQualifiedName~CorruptSignatureTests"` and return
  the verbatim output.
  **Expected red**: the three `A`-ending cases fail (bytes identical). The `Q`/`g`/`w`,
  header/payload and segment-count cases pass.
- [ ] **T002 [US1] (4b, `backend-engineer`, depends on T001)** Rewrite the `CorruptSignature` body per
  plan.md §Design:
  1. Decode the signature.
  2. Flip bit 0 of byte 0.
  3. Re-encode unpadded.
  4. Self-check that the decoded bytes differ.

  Update its doc comment and the fact's `<para>` (the byte-level flip, and one sentence on why the last
  character is unusable). **Do not edit T001's tests or any fact's assertions.** Make T001's filter
  green, then run `dotnet format --verify-no-changes` and a Release build of `tests/Integration.Tests`.
- [ ] **T003 [US1] (5, verify, depends on T002)** Confirm that **no other AppHost/testhost is running**
  (one machine, one stack). Run
  `--filter "FullyQualifiedName~A_malformed_refresh_token_is_refused_without_moving_the_failure_counter"`
  in isolation **8 times** and record the pass/fail count per run. Every run must be green.
- [ ] **T004 [US1] (5, depends on T003)** Run the whole `LockoutSessionSurvivalIntegrationTests` class
  once, to show that SC-1..SC-5 are unaffected by the visibility change. Record the result in the PR's
  verification note, together with the spec §2 probe table.

## Gate notes

- Phase 6: `backend-reviewer`. `/security-review` is **not** required, because no production or auth
  code changes. The PR body must still carry spec §3 (the not-a-security-finding reasoning), so that a
  human reviewer can overrule it.
- The PR closes #2533 with a closing keyword. Check the issue state after the merge.
