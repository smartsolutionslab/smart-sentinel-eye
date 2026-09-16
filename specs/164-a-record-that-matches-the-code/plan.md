# Plan 164 — A record that matches the code

**Spec:** `specs/164-a-record-that-matches-the-code/spec.md`
**Decision of record:** ADR-0152 §Decision 2, 3, 4 + §Implementation Notes.

---

## 1. Shape of the change

**No bounded context gains, loses or changes a layer.** This is the honest
answer and it is worth stating plainly rather than filling the usual headings:

| Usual plan heading | Here |
|---|---|
| Bounded context + layers | **None touched structurally.** `LayoutComposition` keeps Domain / Application / Infrastructure / Api exactly as they are. Four XML doc comments change inside it; no type, member, signature or registration does. |
| Entities / value objects / invariants | **None.** No aggregate, VO, record or invariant is added, removed or altered. The six `*Notification` records in `ILayoutLifecycleBroadcaster.cs` keep byte-identical declarations. |
| Messaging (domain → integration event) | **None.** No domain event, no `Shared.Contracts` message, no Wolverine route, no queue, no outbox row. |
| Boundary rules | **Unchanged and re-asserted.** `BoundaryTests` keeps every assertion body; only its doc comment's stale ADR citation moves. No cross-context reference is created — the deletions remove a project that referenced only `Shared.Kernel`. |
| Persistence | **None.** No EF model, no migration, no Marten store. |
| Auth | **None.** See spec §4.4. |

What *is* designed here is a **removal boundary** and a **correction
vocabulary**. Those are §2 and §3.

---

## 2. The removal boundary

Three artefacts leave. Each has a different failure mode if the boundary is
drawn wrong, so each is drawn separately.

### 2.1 `src/Realtime.Abstractions/` — a project with no referrer

**Goes:** the directory (`SmartSentinelEye.Realtime.Abstractions.csproj`, plus
the untracked `bin/` and `obj/` trees on disk) and `SmartSentinelEye.slnx:106-107`
(the `<Folder>` element and its `<Project>` child — both lines, or the solution
keeps an empty folder node).

**Stays:** `src/Shared.Kernel/`, which the csproj referenced. It has many other
referrers; this removes one unused edge.

**The failure mode if drawn wrong** is not a build break — it is leaving `bin/`
and `obj/` on disk after removing the source. A stale
`SmartSentinelEye.Realtime.Abstractions.dll` in an output folder is exactly the
"restored file keeps its old timestamp" class of confusion: the assembly still
exists locally, nothing rebuilds it, and a later reader finds an artefact for a
project that is gone. **Delete the directory, not just its tracked files.**

**Why nothing notices it leave** (spec §7.2's Q1, settled):

- No `.csproj`, `.props` or `.targets` references it — verified by assembly-name
  search, not path search.
- `scripts/coverage-check.ps1:71` enumerates `tests/**/*.csproj` only. A `src/`
  project is outside its selection; the 90/80/90 gates (ADR-0065) are unmoved.
- `.github/workflows/ci.yml` uses `**/*.csproj` **only inside a
  `hashFiles(...)` NuGet cache key** (lines 41, 146, 235). Removing a csproj
  changes the key once; a cold restore is a slower job, not a failing one.
- **No test counts or enumerates solution projects.** The ~30 `slnx` hits across
  `tests/Architecture.Tests/` are all the same repo-root-finding loop —
  `while (candidate is not null && !File.Exists(Path.Combine(candidate.FullName, "SmartSentinelEye.slnx")))` —
  which needs the file to *exist*, never reads its contents.
- NetArchTest rules load assemblies by explicit name (`Assembly.Load("SmartSentinelEye.ApiGateway")`).
  None loads `Realtime.Abstractions`, so none goes red **and none goes quietly
  vacuous** — there was never an assertion about it to hollow out.

That last point is the interesting one and it is recorded rather than
celebrated: **an unreferenced project sat in the solution for 3.7 months and
nothing in this repository could have noticed.** A guard for that is proposed in
spec §6 and deliberately not built here.

### 2.2 `apps/shared/src/realtime/index.ts` + `surface.pin.ts` — one commit, both files

**Goes:** both files, and the `"./realtime": "./src/realtime/index.ts"` key in
`apps/shared/package.json`.

**Stays, untouched:** `layoutHub.ts`, `layoutHub.test.ts`, `hubUrl.ts`,
`hubUrl.test.ts`, and the two export keys `"./realtime/layoutHub"` and
`"./realtime/hubUrl"`. The `src/realtime/` **directory survives** — only the
barrel and its pin leave it.

**The ordering constraint is a design constraint, not a preference.** There are
three orders and two of them are wrong:

| Order | Result |
|---|---|
| Delete `index.ts` first | `surface.pin.ts` imports `./index.js` → `tsc` red. Loud, recoverable, but it means a commit in the branch does not build — and ADR-0087's rebase-merge lands commits individually on `develop`, so a commit that only compiles with its successor breaks `git bisect` forever (CLAUDE.md §Stacked PRs). |
| Delete `surface.pin.ts` first | `RealtimeClient` and `RealtimeMessage` become unpinned. `tsc` is **green**. This is the silent one, and it is the exact dead-assertion failure specs 159-163 spent a week removing — a window, however short, in which the repository claims a pin it no longer has. |
| **Both in one commit** | The only order with no window in either direction. |

**Therefore: T005 is a single commit containing both deletions and the
`exports` edit.** Not a stylistic preference — the per-commit build obligation
and the no-vacuous-window property both require it.

### 2.3 `apps/management-web/vite.config.ts` — the dead `/hubs` proxy

**Goes:** the proxy entry, the `layoutComposition` const that feeds only it, the
`declare const process` line that exists only for those env reads, and the
comment block (lines ~6-27) explaining a proxy that will no longer exist.

**Stays:** `plugins`, `server.port: 5173`, `strictPort: true`, and the whole
`test:` block.

**`server.proxy` becomes absent rather than `undefined`.** The current
expression is `layoutComposition ? {…} : undefined` — with the const gone, the
key goes too. Leaving `proxy: undefined` behind would be a fragment of the thing
being removed.

**Watch for the cascade:** `tsconfig.base.json` sets `noUnusedLocals: true`, so
removing the proxy while leaving the const is a **typecheck error**, not a
lint warning. The removal is all-or-nothing by the compiler's own rule.

**`apps/kiosk-web/vite.config.ts` is not touched.** Its proxy is live — the
kiosk is the only app with `@microsoft/signalr` and the only one that opens
`/hubs/layouts`.

---

## 3. The correction vocabulary

Eight prose edits. They are listed as **intent**, not as prescribed sentences —
the engineer writes the words. But two rules bind every one of them, because
this spec exists because prose drifted:

**Rule 1 — say the true property, not a softened version of the false one.**
ADR-0152 §3 is precise about what replaced what: the system has **transport
negotiation within SignalR** (WebSockets → SSE → long polling), *not*
**transport replaceability behind an interface**. A correction that says
"SignalR, with a swappable transport" reproduces the error in new words.

**Rule 2 — no corrected text may name a deleted artefact.** `IRealtimeChannel`,
`IRealtimeTransport`, `SmartSentinelEye.Realtime.Abstractions`, `RealtimeClient`
and "a parallel TypeScript interface" must not survive in any live document. A
document describing a seam the same PR deletes is worse than the document was
before.

| # | File | Current claim | Correction intent |
|---|---|---|---|
| 1 | `CLAUDE.md:455` | stack row: "**Replaceable transport** (WebSocket v1, SSE v2 candidate) \| 0076" | SignalR as the real-time transport, with negotiation inside it; cite **0152**. Keep the row's one-line shape — it is a glance table. |
| 2 | `CONTRIBUTING.md:485-490` | names `IRealtimeChannel` / `IRealtimeTransport` in `SmartSentinelEye.Realtime.Abstractions` and a "parallel TypeScript interface in `apps/shared/realtime/`" | Replace the whole bullet. SignalR hub server-side (`LayoutLifecycleHub`, `/hubs/layouts`), `@microsoft/signalr` client-side. **Keep the final sentence** — "Operator commands go over REST, not the realtime channel" — it is still true and still load-bearing. The real seam to name, if one is named, is `ILayoutLifecycleBroadcaster`. |
| 3 | `.claude/agents/frontend-engineer.md:13` | "Realtime (WebSocket, ADR-0076) and WebRTC media stay **direct**, off the gateway" | "Realtime (SignalR, ADR-0152)". **The rest of the sentence is correct and stays** — the off-gateway property is `BoundaryTests`-enforced and unaffected. |
| 4 | `src/LayoutComposition/Domain/Layout/ILayoutLifecycleBroadcaster.cs:3-8` | "ADR-0076 v1 SignalR; future v2 candidates kept swappable **per constitution IX**" and "broadcasts to **all connected admin + kiosk clients**" | **Wrong three ways.** (a) cite ADR-0152; (b) **drop the §IX claim entirely** — §IX's table has four rows and real-time transport is not one, so the citation resolves to nothing; (c) fab-group addressed, **not** all clients. Keep the best-effort / FR-012-reconcile sentence, which is true. This interface **stays** (ADR-0152 §2) — describe it as the send-half seam it is. |
| 5 | `src/LayoutComposition/Infrastructure/Broadcasting/SignalRLayoutLifecycleBroadcaster.cs:8-14` | "Broadcasts to **every connected client (admin or kiosk)**" | **The most consequential correction in the spec.** All six sends are `Clients.Group(LayoutLifecycleHub.FabGroup(…))` (lines 35, 51, 77, 96, 116, 132); `Clients.All` appears nowhere in `src/`. The comment asserts the negation of ADR-0145's fab isolation — a reader trusting it would conclude a Munich frame reaches Dresden. |
| 6 | `src/LayoutComposition/Infrastructure/Broadcasting/LayoutLifecycleHub.cs:8` | "the kiosk (**and management-web**) connects" | Kiosk only. management-web carries no `@microsoft/signalr` dependency and no `/hubs` reference. |
| 7 | `src/AppHost/AppHost.cs:554` | "The realtime **WebSocket** hub (**ADR-0076**, LayoutComposition) … stay direct" | SignalR hub, ADR-0152. **The rest of that comment block stays verbatim** — the `VITE_LAYOUT_HUB_ORIGIN` / POSIX-shell-hyphen explanation is hard-won and still true for the kiosk. |
| 8 | `tests/Architecture.Tests/BoundaryTests.cs:140-141` | "The **per-kiosk WebSocket push (ADR-0076)** … stay direct" | SignalR push, ADR-0152. **The test body does not change** — it still asserts the gateway depends on no `Microsoft.AspNetCore.SignalR`, `…WebSockets` or `…Http.Connections`, all three of which stay correct and wanted. ADR-0152 §Implementation Notes names this test as one that **stays**. |

Plus one header:

| # | File | Correction |
|---|---|---|
| 9 | `docs/adr/0076-realtime-replaceable.md:3` | `**Status:** Accepted` → `**Status:** Superseded by ADR-0152 (<one-line reason>)`, per the convention at `_template.md:3,6`, `0039:3`, `0132:3`. **PR #2403 did not do this** — it touches only `docs/adr/0152-…md`. The body is untouched; a superseded ADR keeps its reasoning. |

**Why #9 is in scope and the other six ADRs citing 0076 are not.** Marking the
superseded document is the supersession *record* and the convention this repo
already follows. Retro-editing ADRs 0074/0075/0106/0107/0111/0112 would be
rewriting history — they cited what was true when written, and ADR-0112's
"SignalR v1" header is evidence of the drift that issue #2400 reports.

---

## 4. Dependencies and sequencing

```
  [ADR-0152 on develop]        ← EXTERNAL BLOCKER, PR #2403
           │
           ▼
      T001  rebase / confirm
           │
   ┌───────┴────────┬──────────────┬──────────────┐
   ▼                ▼              ▼              ▼
  T002 absence    T004 C3        (independent once T001 is green)
  proof           counterfactual
   │                │
   ▼                ▼
  T003 .NET       T005 TS deletions
  removal         (one commit)
   │                │
   └───────┬────────┴──────┬───────────┬──────────┐
           ▼               ▼           ▼          ▼
        T006-T010 doc corrections   T011 vite   (T008 needs T001)
                   │
                   ▼
        T014 C1 + C2 counterfactuals   ← after the doc edits exist
                   │
                   ▼
        T015 full characterisation sweep
                   │
                   ▼
        T012, T013 issue filing (no code, any time)
```

**T001 is a hard external blocker and the only one.** Every other task can start
the moment it is green. ADR-0152 must be on `develop` before T008 (the brief
citation) or `AgentBriefClaimTests.Every_decision_a_brief_cites_exists` goes red
for the right reason at the wrong time (spec §4.2).

---

## 5. Parallelism (ADR-0109)

**Nothing is marked `[P]`, and that is a finding rather than an omission.**

ADR-0109 puts `[P]` on tasks owning **disjoint files**. Here the candidate
split — ".NET half" vs "TypeScript half" — is not disjoint: both halves have a
claim on `CLAUDE.md` and `CONTRIBUTING.md`, the two documents that describe both
sides of the same abstraction in the same bullets. Splitting them means either
two agents editing the same two files or an artificial assignment of a shared
paragraph to one side.

Against that cost, the benefit is ~15 small edits split two ways. The
foundational task (T001) blocks everything anyway, and the characterisation
sweep (T015) must see the whole change at once.

**One agent, sequential.** Stated so the orchestrator does not read the absence
of `[P]` as the architect having forgotten to look.

---

## 6. Risks

| Risk | Likelihood | Handling |
|---|---|---|
| ADR-0152 still not on `develop` when the engineer starts | **Live now** — #2403 is OPEN | T001 is a blocker, not a check. If it is still open, park: this is the ADR-0144 "blocked on an external dependency" shape, not a failure to retry. |
| `AgentBriefClaimTests` red after the brief edit | Moderate | Almost always T001 not done. **Never** weaken, skip or exempt the guard — spec §4.2, and CLAUDE.md's "may not weaken a gate to reach green". |
| A live reference to a deletion subject is discovered | Low (searched two ways) | Refuse and report (spec §4.3). Do not delete the consumer to make room. |
| `noUnusedLocals` breaks the Vite edit half-done | Moderate | Expected and welcome — the compiler enforces §2.3's all-or-nothing. |
| Stale `bin/`+`obj/` left behind for the deleted project | **High if not called out** | §2.1; delete the directory, then confirm a clean Release build. |
| The `surface.pin.ts` retirement is assumed clean | Moderate | C3 (spec §5.2). If the pin turns out already vacuous, that is a **defect in spec 163's work** and must be reported, not absorbed. |
| Someone "helpfully" also removes the AppHost management-web references | Moderate — it looks like the obvious other half | Explicitly out of scope (spec §6). It changes the dev startup graph, which contradicts ADR-0152's "nothing changes at runtime". T013 files it. |

---

## 7. Definition of done

1. The three artefacts in §2 are gone; `layoutHub.ts`, `hubUrl.ts` and their two
   export keys are untouched.
2. The nine corrections in §3 are made; no live document, comment, brief or
   config names `ADR-0076`, `IRealtimeChannel`, `IRealtimeTransport`,
   `SmartSentinelEye.Realtime.Abstractions` or `RealtimeClient`.
3. `docs/adr/0076-*.md` is headed as superseded by ADR-0152.
4. The absence proof (spec §5.1) and three counterfactuals (§5.2) are run and
   their **verbatim output is quoted in the PR body**.
5. Every suite in §5.3 is green before and after with **no assertion edited**.
6. management-web boots and works in the Aspire stack (§5.4).
7. Two issues are filed (backplane, AppHost half) and issue #2400's body figure
   is corrected to 3.7 months.
8. Commits are Conventional, **with no `Co-Authored-By` footer** (ADR-0086).
