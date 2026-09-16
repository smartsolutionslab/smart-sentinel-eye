# Tasks 164 — A record that matches the code

**Spec:** `spec.md` · **Plan:** `plan.md` · **Issue:** #2400 · **ADR:** 0152

**Engineer: `frontend-engineer`, one agent, sequential** (spec §7.1).
Not a JS-only task — the .NET build and `tests/Architecture.Tests` must be run.

**Colour: behaviour-preserving throughout.** Phase 4a is characterisation
(observed green before, unmodified green after) **plus** an absence proof and
three counterfactuals. No test in this spec is expected to go red for a change
in behaviour, because none of the changes adds behaviour (spec §7.3).

**No task is marked `[P]`.** The two candidate halves both claim `CLAUDE.md` and
`CONTRIBUTING.md`, so ADR-0109's disjoint-file basis does not hold — plan §5.

All tasks are `[US1]`; the spec has one user story by design (spec §3).

---

## Phase 0 — Blocker

### T001 [US1] Confirm ADR-0152 is on `develop` and rebase onto it
**Blocks: everything.** `gh pr view 2403 --json state,mergedAt` must show
MERGED, and `git ls-tree origin/develop docs/adr/ --name-only | grep 0152` must
return the file. Then `git fetch origin && git rebase origin/develop`.

If #2403 is still OPEN: **park the issue** as blocked on an external
dependency. Do not start T008, and do not proceed by citing ADR-0152 against a
tree that does not have it — `AgentBriefClaimTests.Every_decision_a_brief_cites_exists`
would go red for a correct edit (spec §4.2).

*Files:* none. *Depends on:* nothing.

---

## Phase 4a — Evidence captured before the change

### T002 [US1] Absence proof for all three deletion subjects
Run the six searches in spec §5.1 verbatim and **save the output**. Two searches
per shape, on the **invariant half**: the .NET project by **assembly name**
(`SmartSentinelEye.Realtime.Abstractions`), not folder path; the TS barrel by
**both** the package specifier (`shared/realtime` with no trailing segment) and
the relative path (`./index.js`, `../realtime`, `realtime/index`).

Expected total: the two `SmartSentinelEye.slnx` lines, the `"./realtime"` key in
`apps/shared/package.json`, and `surface.pin.ts`'s `import … from './index.js'`.
**Anything else is a live reference → stop and report (spec §4.3).**

*Files:* none (output → PR body). *Depends on:* T001.

### T003 [US1] Capture the characterisation baseline, green
Run and save verbatim output for each suite in spec §5.3:
`dotnet build SmartSentinelEye.slnx -c Release`;
`dotnet test tests/Architecture.Tests`;
`dotnet test` for the LayoutComposition unit projects;
`npm run typecheck && npm run lint && npx vitest run` in `apps/shared`,
`apps/kiosk-web`, `apps/management-web`.

All must be **green before anything changes**. A suite that is already red is a
finding to report, not a baseline to work around.

*Files:* none. *Depends on:* T001.

### T004 [US1] Counterfactual C3 — prove `surface.pin.ts` is a live instrument
**Before** deleting anything: add a fourth member to `RealtimeClient` in
`apps/shared/src/realtime/index.ts`, run `npm run typecheck` in `apps/shared`,
and **observe `TS2344` originating in `surface.pin.ts`**. Save the verbatim
failure. Revert the edit and confirm green again.

This is the most skippable and most important task in the spec. If the pin does
**not** fail, it was already vacuous — that is a **defect in spec 163's work**,
must be reported as its own finding, and changes what T005's deletion means.

*Files:* `apps/shared/src/realtime/index.ts` (transient; reverted).
*Depends on:* T003.

---

## Phase 4b — The change

### T005 [US1] Retire `src/Realtime.Abstractions/`
Delete the **directory** — `SmartSentinelEye.Realtime.Abstractions.csproj` plus
the on-disk `bin/` and `obj/` trees, so no orphan assembly survives (plan §2.1).
Remove both `SmartSentinelEye.slnx` lines 106-107: the `<Folder Name="/src/Realtime.Abstractions/">`
element **and** its `<Project>` child.

Then `dotnet build SmartSentinelEye.slnx -c Release` must succeed.

*Files:* `src/Realtime.Abstractions/**` (deleted), `SmartSentinelEye.slnx`.
*Depends on:* T002, T003.

### T006 [US1] Retire the client barrel and its pin — **one commit, both files**
Delete `apps/shared/src/realtime/index.ts` **and**
`apps/shared/src/realtime/surface.pin.ts`, and remove the
`"./realtime": "./src/realtime/index.ts"` key from `apps/shared/package.json`.

**One commit for all three edits** — not a style choice. Deleting `index.ts`
first leaves a commit that does not compile, which ADR-0087's rebase-merge lands
individually on `develop` and which breaks `git bisect` forever. Deleting the
pin first leaves a green window in which the types are silently unpinned — the
dead-assertion shape specs 159-163 removed. Plan §2.2.

**Untouched:** `layoutHub.ts`, `layoutHub.test.ts`, `hubUrl.ts`,
`hubUrl.test.ts`, and the `"./realtime/layoutHub"` and `"./realtime/hubUrl"`
export keys. The `src/realtime/` directory survives.

*Files:* `apps/shared/src/realtime/index.ts` (deleted),
`apps/shared/src/realtime/surface.pin.ts` (deleted),
`apps/shared/package.json`. *Depends on:* T002, T003, T004.

### T007 [US1] Correct `CLAUDE.md`'s stack table row
Line 455. `Real-time push | **Replaceable transport** (WebSocket v1, SSE v2
candidate) | 0076` → SignalR as the transport, with negotiation *inside* it,
citing **0152**. Keep the one-line row shape.

Rule 1 (plan §3) binds: the true property is **transport negotiation within
SignalR**, not "SignalR with a swappable transport".

*Files:* `CLAUDE.md`. *Depends on:* T001.

### T008 [US1] Correct `CONTRIBUTING.md`
Lines 485-490. Replace the bullet wholesale — it names `IRealtimeChannel`,
`IRealtimeTransport`, `SmartSentinelEye.Realtime.Abstractions` and a "parallel
TypeScript interface", all four of which this PR deletes. Describe the SignalR
hub (`LayoutLifecycleHub`, `/hubs/layouts`) and the `@microsoft/signalr` client.

**Keep the final sentence** — "Operator commands go over REST, not the realtime
channel" — still true, still load-bearing.

*Files:* `CONTRIBUTING.md`. *Depends on:* T001.

### T009 [US1] Correct `.claude/agents/frontend-engineer.md`
Line 13: "Realtime (WebSocket, ADR-0076)" → "Realtime (SignalR, ADR-0152)".
**The rest of the sentence stays** — the off-gateway property is
`BoundaryTests`-enforced and unaffected.

**Requires T001 to be genuinely done**: the ADR register resolves the citation
against `docs/adr/*.md`.

*Files:* `.claude/agents/frontend-engineer.md`. *Depends on:* **T001 (hard)**.

### T010 [US1] Correct the four C# doc comments
Plan §3 rows 4-7. Comments only — **no type, member, signature, attribute or
registration changes.**

- `src/LayoutComposition/Domain/Layout/ILayoutLifecycleBroadcaster.cs:3-8` —
  wrong three ways: cite ADR-0152; **delete the "per constitution IX" claim**
  (§IX's table has four rows and real-time transport is not one — spec §7.2);
  and it is **fab-group addressed, not all connected clients**. Keep the
  best-effort / FR-012 sentence.
- `src/LayoutComposition/Infrastructure/Broadcasting/SignalRLayoutLifecycleBroadcaster.cs:8-14` —
  "Broadcasts to every connected client (admin or kiosk)" is the **negation of
  ADR-0145's fab isolation**. All six sends are
  `Clients.Group(LayoutLifecycleHub.FabGroup(…))` (lines 35, 51, 77, 96, 116,
  132); `Clients.All` appears nowhere in `src/`.
- `src/LayoutComposition/Infrastructure/Broadcasting/LayoutLifecycleHub.cs:8` —
  drop "(and management-web)". Kiosk only.
- `src/AppHost/AppHost.cs:554` — SignalR hub, ADR-0152. **The rest of that
  comment block stays verbatim**; the `VITE_LAYOUT_HUB_ORIGIN` /
  POSIX-shell-hyphen explanation is still true for the kiosk.

*Files:* the four above. *Depends on:* T001.

### T011 [US1] Correct `BoundaryTests.cs`'s doc comment
Lines 140-141: "the per-kiosk WebSocket push (ADR-0076)" → SignalR push,
ADR-0152. **The assertion body does not change** — it still asserts the gateway
depends on no `Microsoft.AspNetCore.SignalR`, `…WebSockets` or
`…Http.Connections`, all three correct and wanted. ADR-0152 §Implementation
Notes names this test as one that **stays**.

*Files:* `tests/Architecture.Tests/BoundaryTests.cs`. *Depends on:* T001.

### T012 [US1] Mark ADR-0076 as superseded
`docs/adr/0076-realtime-replaceable.md:3`:
`**Status:** Accepted` → `**Status:** Superseded by ADR-0152 (<one-line reason>)`,
per the convention at `docs/adr/_template.md:3,6`, `0039:3`, `0132:3`.

**PR #2403 did not do this** — it touches only `docs/adr/0152-…md`, so ADR-0076
still reads "Accepted" today. **The body is not edited**; a superseded ADR keeps
its reasoning.

*Files:* `docs/adr/0076-realtime-replaceable.md`. *Depends on:* T001.

### T013 [US1] Remove management-web's dead `/hubs` Vite proxy
`apps/management-web/vite.config.ts`: remove the proxy entry, the
`layoutComposition` const that feeds only it, the `declare const process` line
that exists only for those env reads, and the comment block (~lines 6-27)
describing a proxy that will no longer exist. `server.proxy` goes away entirely
rather than becoming `undefined`.

Keep `plugins`, `server.port: 5173`, `strictPort: true`, and the whole `test:`
block. `tsconfig.base.json`'s `noUnusedLocals: true` makes this all-or-nothing —
the compiler enforces it.

**`apps/kiosk-web/vite.config.ts` is NOT touched.** Its proxy is live.

**Out of scope, do not extend into it:** the matching AppHost half
(`AppHost.cs:571-581`'s `.WithReference(layoutComposition)`,
`VITE_LAYOUT_HUB_ORIGIN`, `.WaitFor(layoutComposition)` on management-web).
Removing a `WaitFor` changes the dev startup graph, contradicting ADR-0152's
"nothing changes at runtime". T016 files it.

*Files:* `apps/management-web/vite.config.ts`. *Depends on:* T003.

---

## Phase 4c — Evidence after the change

### T014 [US1] Counterfactuals C1 and C2 — prove the brief guard is live
**C1:** add an inline-code span naming a now-deleted path (e.g.
`` `src/Realtime.Abstractions/` ``) to any `.claude/agents/*.md`. Run
`dotnet test tests/Architecture.Tests`. Observe
`Every_repository_path_a_brief_quotes_resolves` **fail**, naming the brief.
Save the verbatim failure. Revert.

**C2:** cite `ADR-9999` in `.claude/agents/frontend-engineer.md`. Observe
`Every_decision_a_brief_cites_exists` **fail**. Save. Revert.

Together these show the guard's *silence* about the deleted paths and its
*acceptance* of `ADR-0152` are both real reads rather than unparsed claims —
the distinction spec 161 paid to learn.

*Files:* two briefs (transient; reverted). *Depends on:* T005, T009.

### T015 [US1] Characterisation sweep — green, unmodified
Re-run every suite from T003. All green. **Diff the test files against
`origin/develop`: the only permitted change in any test file is
`BoundaryTests.cs`'s XML doc comment (T011).** An assertion that had to move is
evidence that behaviour moved — block and report, do not adjust.

Also re-run the T002 absence searches: they must now return **nothing at all**.

*Files:* none. *Depends on:* T005-T013.

### T016 [US1] Observe management-web in the Aspire stack
Boot the stack. Open management-web; confirm it loads, authenticates and lists
layouts with its `/hubs` proxy gone. One minute, and the only item in the spec
with a way to break something no suite exercises (spec §5.4).

*Files:* none. *Depends on:* T013.

### T017 [US1] The reader's test
`grep -rn 'ADR-0076' .` (excluding `.git/`, `node_modules/`). Every surviving hit
must be `docs/adr/0076-*.md` itself, `docs/adr/0152-*.md`, another **ADR**, or a
`specs/` artefact. **No live document, comment, brief or config may appear.**
This is the P1 user story, checked directly.

*Files:* none. *Depends on:* T015.

---

## Phase 3 gate / no-code tasks

### T018 [US1] File the scale-out defect (ADR-0152 §4)
New issue: the fan-out is in-process `IHubContext` from Application event
handlers, **no backplane**, single-instance `layout-composition`. A second
replica silently drops frames for clients attached to the other instance.
ADR-0076's per-connection-RabbitMQ design would not have had this. **File it;
do not fix it.** Reference ADR-0152 §4 and #2400. Add to Project #13.

### T019 [US1] File the AppHost management-web half
New issue: `AppHost.cs:571-581` injects `VITE_LAYOUT_HUB_ORIGIN`,
`.WithReference(layoutComposition)` and `.WaitFor(layoutComposition)` into
management-web, which carries no SignalR client and (after T013) no `/hubs`
proxy. Dead, but removing the `WaitFor` changes the dev startup graph — held
out of spec 164 because ADR-0152 states nothing changes at runtime. Add to
Project #13.

### T020 [US1] Correct issue #2400's body
`gh issue edit 2400` — "roughly fifteen months" → **3.7 months** (ADR-0076
2026-05-25 → SignalR in `82ae74e9` two days later; divergence closed
2026-09-16), and soften "nobody wrote down why": the decision **path** is
recorded at `specs/003-layout-composition/spec.md:467-470` and `plan.md:54`;
the **reason** is what is missing. Per ADR-0152 §Implementation Notes.

The same figure at `surface.pin.ts:33` corrects itself — T006 deletes the file.
The two instances in `specs/162/spec.md` are **historical artefacts and are not
edited**.

### T021 [US1] Board gate
The feature issue (#2400) is on Project #13 — it already shows "In Progress".
Verify with `gh project item-list 13 --owner smartsolutionslab --limit 2000`;
**the default limit of 30 makes a filled board look empty.** No per-task issues
(CLAUDE.md: `/speckit-taskstoissues` is not the default since spec 028).

---

## Commit plan

Conventional Commits, **no `Co-Authored-By` footer** (ADR-0086 — this overrides
any attribution reminder in the agent's context). Each commit builds on its own.

| # | Message | Tasks |
|---|---|---|
| 1 | `chore(solution): retire the empty Realtime.Abstractions project (ADR-0152)` | T005 |
| 2 | `refactor(shared): retire the unimplemented RealtimeClient barrel and its pin` | T006 |
| 3 | `docs: correct the real-time transport record to SignalR (ADR-0152)` | T007, T008, T009, T012 |
| 4 | `docs(layouts,apphost): correct the transport and fan-out claims in code comments` | T010, T011 |
| 5 | `chore(management-web): drop the dead /hubs dev proxy` | T013 |

Commit 2 is indivisible (plan §2.2). Commits 3 and 4 may merge if the engineer
prefers; 1, 2 and 5 may not merge into anything, because each is a distinct
deletion boundary with its own evidence.
