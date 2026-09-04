# Tasks: A variable change reaches the tile

**Spec**: `specs/063-a-variable-change-reaches-the-tile/spec.md` ·
**Plan**: `plan.md` · **Issue**: #2012 · **Lane**: ADR-0144 autonomous.

**Phase 4a colour: RED** (behaviour-changing, plan.md declaration 3).

---

## Parallelism (ADR-0109)

There are **no foundational tasks** — no `Shared.Kernel`, `Shared.Contracts`,
`AppHost` or Aspire-resource change blocks anything. The contract record and the
metadata envelope already exist; only a value changes.

The three red tests own **disjoint files** in **three different projects** and
can be written concurrently:

| | Project | File |
|---|---|---|
| T001 | `SystemVariables.Application.Tests` | two existing handler test files |
| T002 | `Integration.Tests` | one new file |
| T003 | `kiosk-web` | `CellPage.test.tsx` |
| T004 | `e2e` | `kiosk-shows-a-label-over-video.spec.ts` |

T001–T004 are all `[P]`.

**What is strictly sequential** is colour before change: **every** red must be
*observed and captured* before T005 touches production code. That is the whole
of ADR-0144's phase-4 split, and it is not a scheduling preference.

**Practical note for the orchestrator:** T002 needs the Docker stack and T004
needs the full run-mode stack with the video fixture. They are the long poles.
Start them first, and let T001 and T003 finish inside their runtime.

---

## Task list

### T001 [P] [US1] [US2] — the producer red, both handlers (phase 4a) — `test-writer`

Add one assertion to each of the two existing tests that already pull the
published `ResolvedOverlayTextChangedV1` off the fake bus. Both files already
construct their domain event with `FabIdentifier.From("munich")`, so nothing new
is set up.

- `tests/SystemVariables.Application.Tests/EventHandlers/VariableValueChangedDomainEventHandlerTests.cs`
  → `Publishes_V1_event_and_a_resolved_text_event_for_each_affected_overlay`
- `tests/SystemVariables.Application.Tests/EventHandlers/VariableArchivedDomainEventHandlerTests.cs`
  → the two tests at lines ~69 and ~100 that bind `push`

Assert that the pushed event's `Metadata.Fab` is `"munich"`.

**Expected red:** `Expected push.Metadata.Fab to be "munich" but was null`.

**Do not** add a test asserting the *presence* of metadata — that would pass
today. The assertion is about the value.

Capture the verbatim failure. Command:

```sh
dotnet test tests/SystemVariables.Application.Tests/SmartSentinelEye.SystemVariables.Application.Tests.csproj \
  --filter "FullyQualifiedName~VariableValueChangedDomainEventHandlerTests|FullyQualifiedName~VariableArchivedDomainEventHandlerTests"
```

*(Baseline taken during phase 1: 6 passed, 0 failed. Any other starting number
means something else changed and should be reported before proceeding.)*

**Depends on:** nothing.

---

### T002 [P] [US1] — the seam red: a real frame off a real hub (phase 4a) — `test-writer`, with `test-adversary` for the negatives

**New file:** `tests/Integration.Tests/SystemVariables/ResolvedTextReachesItsFabTests.cs`

Read `tests/Integration.Tests/LayoutComposition/OverlayFrameFabScopingIntegrationTests.cs`
first and mirror it — per-fab `HubConnection`s, per-operator tokens, the
`ConcurrentDictionary<Guid, TaskCompletionSource<…>>` collector, and a named
`SilenceWindow` constant for the frame that must not arrive. Reuse
`VariableRequests.SetValueAsync` (`tests/Integration.Tests/Fixtures/VariableRequests.cs`)
and the resolvable-wait shape from `NFR_VariableResolutionLatencyTests`.

`InitializeAsync` resets SystemVariables, LayoutComposition and OverlayDesigner —
all three reset helpers exist on `AspireFixture`.

**Test 1 — the frame arrives, in the fab it belongs to (FR-001, FR-003, FR-006)**

1. Define a munich variable; publish an overlay whose text embeds its
   placeholder; publish a munich layout referencing it.
2. Wait until `GET /system-variables/snapshot` resolves the overlay — the
   reverse index is populated by an integration event.
3. Connect a munich hub client and a dresden hub client, both listening on
   `nameof(ILayoutLifecycleClient.ResolvedOverlayTextChanged)`.
4. `PUT /system-variables/{name}/value`; start a `Stopwatch` when it returns.
5. Await the munich frame; stop the clock; assert the frame carries the new
   resolved text.
6. **Print the figure** — `Console.WriteLine`, the same artefact style
   `NFR_VariableResolutionLatencyTests` uses, naming constitution §IV's
   *event → overlay state* leg and its 200 ms budget, and stating that the
   figure **excludes the browser**.
7. Assert on the figure only as an **order-of-magnitude** guard, with a comment
   saying so. Do not assert 200 ms: that would police a budget with an
   instrument that includes CI's cold JIT and container scheduling, and it is
   the assertion that later gets deleted.

**Test 2 — and nowhere else (FR-003, `test-adversary`)**

The dresden connection receives nothing within the silence window. This must not
be the assertion that makes the file red today; it passes today for the wrong
reason (nothing is sent to anyone), and the file's docblock must say so
explicitly so a future reader does not mistake it for coverage that existed.

**Test 3 — a refused write pushes nothing (`test-adversary`)**

`PUT` an invalid value for the variable's type → 4xx, and no frame on either
connection within the silence window.

**Expected red:** test 1 times out awaiting the munich frame. Because a timeout
is a weak signal, the assertion message must name what to look for:

> no ResolvedOverlayTextChanged frame reached the munich connection — check the
> LayoutComposition log for `ResolvedOverlayTextChangedWithoutFab`, which means
> the producer published the event with no fab

Capture the verbatim failure.

**Depends on:** nothing. **Blocks:** T005.

---

### T003 [P] [US1] — the frontend hop, colour unknown, reported either way (phase 4a) — `test-writer`

`apps/kiosk-web/src/features/cell/CellPage.test.tsx`.

Capture the `onResolvedOverlayTextChanged` callback the way
`apps/kiosk-web/src/features/revocation/useLayoutLifecycle.test.tsx` already
does, render a wall whose tile binds an overlay with a `{{…}}` placeholder and a
seeded snapshot, invoke the callback with a higher `version`, and assert the
rendered `camera-viewer-overlay-label` carries the new text.

**This test is expected GREEN on first run.** It is declared so here, in advance,
so it cannot be mistaken for a phase-4a red artifact or for a shortcut. It exists
because the hop it covers has never once been executed by any test.

**Report its colour verbatim, whichever it is.** If it is red, stop and say so:
that is a second, independent defect, it belongs to `frontend-engineer`, and the
orchestrator needs to know before T005 lands a fix that will then look ineffective.

**Do not** adjust the assertion to match what the component does. That is
forbidden in both of ADR-0144's colours.

**Depends on:** nothing.

---

### T004 [P] [US1] — the e2e red: a tile, real video, an operator (phase 4a) — `test-writer`

`e2e/kiosk-shows-a-label-over-video.spec.ts`: change

```
test.fixme('the span from a value being submitted to it being visible', …
```

to `test(`. **One word. Nothing else in the file changes** — not the timeouts,
not the iteration count, not the report, not the refusal path.

Also delete the paragraph in that test's docblock that explains why it is held
back (it will no longer be true), leaving the rest of the docblock intact. Do
this in T006, not here — at 4a the docblock must still say why it is red.

Run it and capture the output verbatim, including the `[span]` report lines.

**Expected red:**

```
iteration 0: the value never reached the tile within 60000ms
[span] UNMEASURED — iteration 0: the value never reached the tile within 60000ms
```

**Read the printed figures, not a median** (spec.md, #2014 interaction).

**Depends on:** nothing. **Blocks:** T005.

---

### T005 [US1] [US2] — the fix (phase 4b) — `backend-engineer`

Brief: the verbatim red output from T001, T002 and T004.

Two lines, exactly as plan.md specifies:

- `src/SystemVariables/Application/EventHandlers/VariableValueChangedDomainEventHandler.cs:72`
  — `null` → `fab.Value`
- `src/SystemVariables/Application/EventHandlers/VariableArchivedDomainEventHandler.cs:104`
  — `null` → `fab.Value`

**Constraints, all of them load-bearing:**

- **Do not touch any test written in T001–T004.** Not an assertion, not a
  timeout, not a message.
- **Do not touch the consumer's guard** in `ResolvedOverlayTextChangedV1Handler`.
  Deleting it would make T002's first test pass and its second test fail, which
  is the cross-fab leak this whole design is arranged to prevent.
- **Do not** also fix `SystemVariableArchivedV1`'s null fab in the same file.
  Out of scope, spec.md says why, and it is a separate issue.

Re-run T001, T002, T003 and observe green. Capture the output.

**Depends on:** T001, T002, T004 (each observed red and captured). T003's colour
must be **known** before this starts, whatever it is.

---

### T006 [US1] — the two records that hid the gap (phase 4b) — `backend-engineer`

Not a drive-by comment. Both of these are false statements that helped this
defect survive, and both are now falsified by a test in this same change.

1. `tests/Integration.Tests/SystemVariables/NFR_VariableResolutionLatencyTests.cs`
   — the docblock says the SignalR hop "is covered separately" by
   `OverlayPushIntegrationTests`, which covers a **different frame**. Point it at
   `ResolvedTextReachesItsFabTests` (FR-008).
2. `e2e/kiosk-shows-a-label-over-video.spec.ts` — remove the "held back" paragraph
   from the span test's docblock, now that it is not held back. Keep everything
   the docblock says about what the span does and does not cover; that is still
   true and is the part that matters.

**Depends on:** T005.

---

### T007 [US1] — verify, and write the figure down (phase 5) — `backend-engineer` or orchestrator

1. Re-run T004's e2e and capture the full `[span]` report.
2. Run the manual procedure in spec.md, or at minimum its step 7: confirm
   `ResolvedOverlayTextChangedWithoutFab` no longer appears in the
   LayoutComposition log on a value change and `BroadcastResolvedOverlayTextChanged`
   does.
3. Write `verification.md` recording:
   - the **leg**: *event → overlay state*, ≤ 200 ms (constitution §IV);
   - T002's server-side figure, with its stated exclusion of the browser;
   - the e2e span's per-iteration figures — **the list, not the median** — with
     iteration 0 flagged if it is an outlier (#2014);
   - the explicit statement that **§IV's table is not being edited by this run**
     and why (ADR-0144), with a recommendation for what a human might change;
   - the recommendation to file `SystemVariableArchivedV1`'s null fab as its own
     issue, with the audit-row evidence.

**Depends on:** T005, T006.

---

## Dependency graph

```
T001 [P] ─┐
T002 [P] ─┼─ all observed red / colour known ─→ T005 ─→ T006 ─→ T007
T003 [P] ─┤
T004 [P] ─┘
```

---

## Commits (ADR-0030 Conventional Commits · ADR-0086 **no `Co-Authored-By`**)

Each commit must build and pass on its own — rebase-merge lands them
individually on `develop` (ADR-0087).

1. `test(system-variables): a resolved-text push names the fab it belongs to`
   *(T001 — red)*
2. `test(integration): a resolved-text frame reaches its fab and no other`
   *(T002 — red)*
3. `test(kiosk): a pushed resolved text reaches the tile that binds the overlay`
   *(T003)*
4. `test(e2e): the span from a submitted value to a visible one runs again`
   *(T004 — red)*
5. `fix(system-variables): a resolved-text push carries the fab that changed`
   *(T005 — green)*
6. `docs(tests): the cross-reference names the test that covers the hop`
   *(T006)*

Commits 1, 2 and 4 land red. That is deliberate and is what ADR-0139 asks for;
the PR body quotes their output. **CI will be red on those commits** — the
orchestrator opens the PR after commit 5, so the PR's head is green while its
history carries the evidence.

---

## Phase 3 gate (CLAUDE.md, as corrected 2026-08-28)

Per-task issues are **not** created — that stopped after spec 028. The gate is
that the **feature's** issue is on Project #13:

```sh
gh project item-add 13 --owner smartsolutionslab --url https://github.com/smartsolutionslab/smart-sentinel-eye/issues/2012
```

**#2012 is already on the board with status *In Progress*** (`gh issue view 2012`
reports `projects: Smart Sentinel Eye (In Progress)`). Nothing to add. Do not run
`/speckit-taskstoissues`.
