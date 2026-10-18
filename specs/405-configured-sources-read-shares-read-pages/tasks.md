# Tasks 405: the configured-sources read shares ReadPagesAsync

**Spec**: [spec.md](./spec.md). **Plan**: [plan.md](./plan.md). **Issue**: #3021 (feature-level; already
on Project #13; no per-task issues).

**No decision gate.** Spec §2 found no observable difference between the two loops, so nothing needs the
owner's choice.

**Phase 4a colour: CHARACTERISATION, observed green** (behaviour-preserving). T001 adds C1-C6 and runs them
together with the existing covering tests on unchanged code (`b7001237`). The output is quoted verbatim in
the PR. After T010 the same command must pass with no `tests/` file modified. An assertion that would have to
change is a **block**, not an edit (ADR-0144).

**Roles:** test-writer T001 (writes the one new test file, runs, returns verbatim output). backend-engineer
T010. Phase 6: `/code-review` (backend-reviewer). No security review: spec header.

**Forbidden in every task:** editing any existing file under `tests/`; editing `ReadPagesAsync`, `PageRead`,
`PageReadEnd`, any `AddPageTo*`, `GetAllPathsHealthAsync`, `ListConfiguredPathsAsync`'s code, `Log.cs`,
`IRtspGateway.cs`; changing `MaxListPages`'s value; a deadline or linked CTS; any suppression; ADRs, the
constitution, CLAUDE.md.

Legend: `[P]` = owns disjoint files (ADR-0109). No foundational task. T001 → T010 is strictly sequential (the
characterisation must exist and be green before the code moves). Nothing here parallelises usefully.

**Test filter (T001, T020):**

```sh
dotnet test tests/StreamDistribution.Infrastructure.Tests -c Release --filter "FullyQualifiedName~MediaMtxRtspGatewayConfiguredSources|FullyQualifiedName~MediaMtxSourceRepairOverRealGatewayTests|FullyQualifiedName~MediaMtxRtspGatewayPaginationTests|FullyQualifiedName~MediaMtxRtspGatewayListReaders|FullyQualifiedName~StreamHealthWatcherCutShortReadTests|FullyQualifiedName~MediaMtxSourceRepair"
```

`MediaMtxRtspGatewayConfiguredSources` matches the three existing classes and the new
`...ConfiguredSourcesFoldCharacterisationTests`. `MediaMtxSourceRepair` picks up the Reconciler/sweep tests,
which use fakes. They are a cheap regression net for the caller, not characterisation of the fold.

## Phase 4a: characterisation (test-writer, on `b7001237`)

- [ ] **T001 [US1]** Create
  `tests/StreamDistribution.Infrastructure.Tests/Gateways/MediaMtxRtspGatewayConfiguredSourcesFoldCharacterisationTests.cs`.
  It is a real `MediaMtxRtspGateway` over a stub `HttpMessageHandler` that records each
  `RequestUri.PathAndQuery`, plus the existing `CapturingLogger<MediaMtxRtspGateway>`. Mirror the stub and
  `Body`/`Item` helpers of `MediaMtxRtspGatewayConfiguredSourcesBoundsTests`, including the 1,100-request
  ceiling. Sentence-style names (ADR-0053). One test per gap in spec §4:
  - **C1** `pageCount` `"1.5"` only on page 2 (pages 0-1 report `"5"`, one canonical item each). Requests
    `[0,1,2]`. Three sources. Exactly one Warning. Its message contains `"on page 2"` and
    `"the source repair compares only the paths read so far"`, and does not contain `"startup reconcile"`.
  - **C2** Every page `pageCount` `2147483647` plus one item. Exactly one Warning. It contains
    `"after 1000 pages"` and `"the source repair"`, and not `"startup reconcile"`.
  - **C3** Three pages, `pageCount` `"3"`. The recorded `PathAndQuery` list equals
    `["/v3/config/paths/list?page=0&itemsPerPage=100", "...page=1...", "...page=2..."]` exactly.
  - **C4** Page 0 OK (`pageCount` `"3"`), page 1 answers `503`. `Should.ThrowAsync<HttpRequestException>`
    with `StatusCode == ServiceUnavailable`. Requests `[0,1]`. `logger.Entries` empty.
  - **C5** Page 0 `{"pageCount":1.5,"items":[]}`. Requests `[0]`. Empty result. No entry at Warning or above.
  - **C6** Page 0 body `[]`. `Should.ThrowAsync<InvalidOperationException>`. Requests `[0]`. Add a one-line
    `why` in the class summary: this pins today's behaviour and does not endorse it (spec A1).

  Then run the **Test filter** above on unchanged `src/`, and return the **verbatim** output. Every test
  must be green. A red C-test on `b7001237` means the test or spec §2 is wrong: stop and report. Do not touch
  `src/`. Commit `test(3021): characterise the configured-sources read before it moves onto ReadPagesAsync`
  (tests only, no Co-Authored-By, ADR-0086).

## Phase 4b: implement (backend-engineer)

- [ ] **T010 [US1]** In `src/StreamDistribution/Infrastructure/Gateways/MediaMtxRtspGateway.cs`, replace
  `ListConfiguredPathSourcesAsync`'s body with plan §2's code. Delete `MaxConfiguredSourcePages`. Reword the
  method's comment (spec A2). Optionally widen the "shares ReadPagesAsync with GetAllPathsHealthAsync" comment
  in `ListConfiguredPathsAsync` to name all three readers (comment only). Run `dotnet format whitespace` and
  `dotnet format style --verify-no-changes --diagnostics IDE0005` on
  `src/StreamDistribution/Infrastructure`. Depends on T001. Commit
  `refactor(3021): the configured-sources read uses ReadPagesAsync and MaxListPages`.

## Phase 5: verify

- [ ] **T020 [US1]** Re-run the **Test filter** unchanged; all green; `git diff <T001 commit> -- tests/` is
  empty. Quote both outputs (T001's and this one) in the PR.
- [ ] **T021 [US1]** Structural checks (plan §4.3): `git grep -n MaxConfiguredSourcePages src` is empty;
  `git grep -c "for (var page = 0;" src/StreamDistribution/Infrastructure/Gateways/MediaMtxRtspGateway.cs`
  is `1`; the `ReadPagesAsync` hunk is absent from `git diff b7001237 -- src/`.
- [ ] **T022 [US1]** Counterfactual (plan §4.4), not committed. Swap in the Paths unreadable logger and see C1
  red. Swap in the Paths cap logger and see C2 red. Change the query to `itemsPerPage=50` and see C3 red.
  Revert and see green. Quote the three failure lines in the PR.
- [ ] **T023** Release build of `src/StreamDistribution/Infrastructure` (no new warning in that project; no
  IDE0051). Note in the PR that #3029's S138 on `GetAllPathsHealthAsync` is unchanged by design (spec §7).

## Phase 6-7

- [ ] **T030** `/code-review`: the diff is one method, one deleted constant and comments. Check the reviewer
  confirms the Sources (not Paths) logger methods and an untouched `ReadPagesAsync`.
- [ ] **T031** PR to `develop` (`--base develop`) with a closing keyword for #3021 **only if** the owner
  agrees items 2-5 move to their own issues. Otherwise use `Refs #3021` and leave the issue open for them.
  Afterwards comment on #3029 that it can rebase now. Phase 4a colour: characterisation.
