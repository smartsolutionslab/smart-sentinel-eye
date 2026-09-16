# Spec 164 — A record that matches the code

**Issue:** #2400
**Branch:** `chore/2400-a-record-that-matches-the-code`
**Decisions of record:** **ADR-0152** (SignalR is the real-time transport; supersedes
ADR-0076) — §Decision 2, 3, 4 and §Implementation Notes are the whole brief.
Secondary: ADR-0145 (fab isolation, which one stale comment contradicts), ADR-0086
(no `Co-Authored-By`), ADR-0037 (phases), ADR-0144 (autonomous lane), ADR-0109
(`[P]` on disjoint files).
**Constitution:** §IX (Forward-Compatible Strategy Interfaces) — checked and
**not** engaged; see §7.2.

---

## 1. Why

ADR-0076 decided the real-time transport is **replaceable behind an
abstraction**, specified **native WebSocket** for v1, and listed **SignalR under
rejected alternatives**. The repository ships SignalR. ADR-0152 closed that
contradiction in SignalR's favour on 2026-09-16.

This spec is the **implementation of ADR-0152**. It makes no decision. It
removes two abstractions that were scaffolded and never built, corrects every
document and comment that still asserts the superseded design, and files the one
genuine defect ADR-0152 declined to fix here.

**Two figures in issue #2400's body are wrong and this spec corrects one of
them.** The divergence is **3.7 months** (ADR-0076 landed 2026-05-25, the
repository's first day; SignalR landed two days later in `82ae74e9`), not
fifteen. And "nobody wrote down why" is too strong — the decision *path* is
recorded at `specs/003-layout-composition/spec.md:467-470` and `plan.md:54`;
what is missing is the *reason*, and the acknowledgement that ADR-0076 rejected
SignalR.

**Nothing in this spec changes runtime behaviour.** ADR-0152 says so explicitly
and this spec holds itself to it — see §6, which is why one otherwise-tempting
cleanup is pushed out to an issue instead.

---

## 2. Premise, verified at the tip

Every row below was re-checked on `develop` at `67c64b36` rather than inherited
from the investigation. **Four rows changed the scope.**

| # | Claim | Verdict | Evidence at the tip |
|---|---|---|---|
| 1 | `src/Realtime.Abstractions/` holds zero `.cs` files | **Confirmed** | `find src/Realtime.Abstractions -name '*.cs' -not -path '*/obj/*' -not -path '*/bin/*'` → 0. The six `.cs` hits are MSBuild-generated under `obj/`. |
| 2 | Referenced by no `.csproj` | **Confirmed** | A repo-wide grep for `Realtime.Abstractions` returns exactly 10 hits: `index.ts:29` (a comment), `CONTRIBUTING.md:488`, `docs/adr/0076:20`, `SmartSentinelEye.slnx:106-107`, and five in `specs/162/`. **No `ProjectReference`, anywhere.** |
| 3 | `RealtimeClient` has zero implementors and zero consumers | **Confirmed, with the expected exception** | The only importer of `./index.js` is its own sibling `surface.pin.ts` — which is *why* the pin must go in the same change. All seven live references take `shared/realtime/layoutHub`; one takes `/hubUrl`. |
| 4 | The `"./realtime"` export has zero importers | **Confirmed** | No file in `apps/` resolves `@smart-sentinel-eye/shared/realtime` without a trailing segment. `apps/shared/src/index.ts` re-exports only `./types/index.js`, so the `.` specifier does not carry it either. |
| 5 | `CLAUDE.md`, `CONTRIBUTING.md`, `frontend-engineer.md` assert the superseded design | **Confirmed** | `CLAUDE.md:455`; `CONTRIBUTING.md:485-490`; `.claude/agents/frontend-engineer.md:13`. |
| 6 | `ILayoutLifecycleBroadcaster` doc comment restates the drift | **Confirmed, and it is wrong a second way** | It says "ADR-0076 v1 SignalR; future v2 candidates kept swappable **per constitution IX**". §IX's table has four rows and the real-time transport is **not one of them** — see §7.2. |
| 7 | `LayoutLifecycleHub.cs:8` claims management-web connects | **Confirmed** | `apps/management-web/` has zero `@microsoft/signalr` dependency, zero `/hubs` references in `src/`. Only `kiosk-web/package.json:15` carries the client. |
| 8 | `apps/management-web/vite.config.ts` proxies `/hubs` for nothing | **Confirmed** | Lines 17-36. No management-web code requests it; the two e2e specs that route `**/hubs/**` are both kiosk specs. |
| **9** | *(new)* `ADR-0076`'s own header still reads `**Status:** Accepted` | **Found** | `docs/adr/0076-realtime-replaceable.md:3`. PR #2403 touches **only** `docs/adr/0152-…md` — it did not update the superseded document. The repo convention is `**Status:** Superseded by ADR-NNNN (<reason>)`, e.g. `0039:3`, `0132:3`, and `_template.md:3,6`. |
| **10** | *(new)* `SignalRLayoutLifecycleBroadcaster.cs:11` says "Broadcasts to **every** connected client (admin or kiosk)" | **Found, and it contradicts ADR-0145** | All six sends are `Clients.Group(LayoutLifecycleHub.FabGroup(…))` at lines 35, 51, 77, 96, 116, 132. **`Clients.All` appears nowhere in `src/`.** The comment asserts the opposite of the fab isolation ADR-0145 rests on. Same error in `ILayoutLifecycleBroadcaster`'s summary ("broadcasts to all connected admin + kiosk clients"). |
| **11** | *(new)* `src/AppHost/AppHost.cs:554` cites the superseded design | **Found** | "The realtime **WebSocket** hub (**ADR-0076**, LayoutComposition) … stay direct". |
| **12** | *(new)* `tests/Architecture.Tests/BoundaryTests.cs:140-141` cites it too | **Found** | "The **per-kiosk WebSocket push (ADR-0076)** … stay direct". The *test* stays (ADR-0152 §Implementation Notes); only its doc comment is stale. |

**Two claims from the brief did not survive as written.** The "fifteen months"
figure appears at `apps/shared/src/realtime/surface.pin.ts:33` as **"15
months"** — and that file is deleted by this spec, so the figure corrects itself
by removal rather than by edit. It also appears twice in
`specs/162-tests-that-actually-run/spec.md` (lines 304, 396), which is a
**historical artefact and is deliberately not edited**: a spec records what was
believed when it was written. The one live instance that must be edited by hand
is **issue #2400's own body**.

---

## 3. User stories

### P1 — The reader who lands on the real-time transport (the whole slice)

> As an engineer (or an autonomous agent, which has less context and no way to
> notice) reading this repository to find out how real-time push works, I want
> every document, comment and declaration I encounter to describe SignalR —
> the thing that actually runs — so that my premise is right before I write
> anything.

This is the smallest independently-shippable vertical and it is also the whole
feature. It is not decomposable further in a useful way: shipping the deletions
without the document corrections leaves documents pointing at files that no
longer exist (a *worse* state than today), and shipping the corrections without
the deletions leaves the corrected documents describing a seam that still sits
in the tree. The unit of value is "the record matches the code", and half of it
is not half the value.

**No P2, no P3.** Both candidates were considered and are out of scope with
reasons — see §6.

---

## 4. Acceptance scenarios

### 4.1 Happy path — the record matches the code

```gherkin
Scenario: the abstractions are gone and the solution still builds
  Given src/Realtime.Abstractions/ holds zero .cs files and no .csproj references it
  When the project directory and its two SmartSentinelEye.slnx lines are removed
  Then `dotnet build SmartSentinelEye.slnx -c Release` succeeds
  And no compiler warning or analyzer error names Realtime.Abstractions
  And the Architecture.Tests suite passes with every test unmodified

Scenario: the client-side abstraction is gone and the workspaces still typecheck
  Given apps/shared/src/realtime/index.ts has exactly one importer, its own sibling pin
  When index.ts, surface.pin.ts and the "./realtime" key are removed together
  Then `npm run typecheck` passes in apps/shared, apps/kiosk-web and apps/management-web
  And `npm run lint` passes with --max-warnings 0
  And `vitest run` passes in every workspace with no test file edited
  And apps/shared/src/realtime/layoutHub.ts and hubUrl.ts are untouched
  And the "./realtime/layoutHub" and "./realtime/hubUrl" export keys remain

Scenario: every corrected document names ADR-0152
  Given ADR-0152 is on develop and this branch is rebased onto it
  When CLAUDE.md, CONTRIBUTING.md and .claude/agents/frontend-engineer.md
       are corrected to describe transport negotiation within SignalR
  Then AgentBriefClaimTests passes with all 14 facts unmodified
  And CLAUDE.md's stack table row cites 0152, not 0076
  And no corrected document names IRealtimeChannel, IRealtimeTransport or RealtimeClient
```

### 4.2 Conflict — the ordering hazard, which is real and has a named guard

```gherkin
Scenario: citing ADR-0152 before ADR-0152 exists on the branch
  Given PR #2403 is OPEN and docs/adr/0152-*.md is absent from develop
  And AgentBriefClaimTests.Every_decision_a_brief_cites_exists reads the ADR
      register as the union of docs/adr/*.md filenames and the 27 rows of
      docs/adr/0000-initial-decisions.md
  When .claude/agents/frontend-engineer.md is edited to cite ADR-0152
  Then that test fails, naming frontend-engineer.md and ADR-0152
  And the correct response is to rebase onto a develop that carries ADR-0152,
      never to weaken, skip or suppress the guard
```

This is not hypothetical bookkeeping. It is the one way this spec can go red for
a reason that has nothing to do with its own correctness, and it has a
deterministic fix that an agent must not mistake for a failing edit.

### 4.3 Refusal — a live reference is discovered

```gherkin
Scenario: a deletion subject turns out to have a consumer
  Given the engineer runs the absence proof in §5.1 before deleting anything
  When any search returns a reference that is not the subject's own sibling pin
       or its own doc comment
  Then the deletion is refused and the finding is reported
  And the change is not forced through by deleting the consumer as well
```

### 4.4 Authorization — the hub's scope enforcement does not move

```gherkin
Scenario: no authorization surface changes
  Given LayoutLifecycleHub carries [Authorize(Policy = Scope.Sse.Layouts.Read)]
  And EndpointScopeDeclarationTests registers /hubs/layouts as the sole route
      mapped outside the readable shapes, bound to typeof(LayoutLifecycleHub)
  When this change lands
  Then that register row is byte-identical
  And BoundaryTests.ApiGateway_does_not_sit_on_the_realtime_or_media_latency_legs
      passes with its assertion body unmodified
  And no scope constant, policy, realm client or token audience is added,
      removed or renamed
```

Stated rather than skipped because a spec that deletes a project named
`Realtime.Abstractions` in a repository where the real-time hub is the one
route authorizing outside the fluent chain should say, explicitly, that it goes
nowhere near that route's auth. It does not.

---

## 5. Independent end-to-end test procedure

An independent reviewer runs this without reading the diff.

### 5.1 The absence proof (run BEFORE the deletions, captured in the PR)

Green tests after a deletion prove the covering tests do not object. They do
**not** prove the deleted thing was dead — the suite would be equally green if
something live had been deleted with no coverage. The positive evidence is a
mechanical absence proof, searched on the **invariant half of each shape**:

```sh
# The .NET project — search the ASSEMBLY NAME, not the folder path.
# A ProjectReference can spell the path a dozen ways; it cannot avoid the name.
grep -rn 'SmartSentinelEye\.Realtime\.Abstractions' --include='*.csproj' \
     --include='*.props' --include='*.targets' --include='*.slnx' --include='*.cs' . \
  | grep -v '/obj/' | grep -v '/bin/'
# Expect: the two SmartSentinelEye.slnx lines and nothing else.

# The TS barrel — search BOTH halves: the package specifier and the relative path.
grep -rn "shared/realtime'" apps/ --include='*.ts' --include='*.tsx' | grep -v node_modules
grep -rn 'shared/realtime"' apps/ --include='*.ts' --include='*.tsx' --include='*.json' | grep -v node_modules
grep -rn "from '\.\./realtime'\|from '\./realtime'\|realtime/index" apps/ | grep -v node_modules
# Expect: only apps/shared/package.json's own "./realtime" key,
# and surface.pin.ts's import of './index.js'.
```

### 5.2 Counterfactuals — three, because "it stayed green" is the experiment spec 161 warned about

Each is constructed, observed failing, then reverted. **Verbatim output quoted
in the PR.** Without these, the guards this change leans on are asserted rather
than shown to be live.

| # | Construct | Must fail | Proves |
|---|---|---|---|
| **C1** | After the deletions, add an inline-code span naming a deleted path (e.g. `` `src/Realtime.Abstractions/` ``) to any `.claude/agents/*.md` | `AgentBriefClaimTests.Every_repository_path_a_brief_quotes_resolves` | The path guard is **live, not vacuous** — so its silence about the deleted paths is real evidence, not an unparsed claim |
| **C2** | Cite `ADR-9999` in `.claude/agents/frontend-engineer.md` | `AgentBriefClaimTests.Every_decision_a_brief_cites_exists` | The new `ADR-0152` citation is **checked**, not walked past — the register resolves it rather than pattern-matching four digits |
| **C3** | **Before** deleting: add a fourth member to `RealtimeClient` in `index.ts` | `tsc --noEmit` in `apps/shared` with `TS2344` from `surface.pin.ts` | The pin being retired **was live** (`apps/shared/tsconfig.json` includes `src/**/*`, so it is typechecked). Confirms we are removing a working instrument together with its subject — not discovering it was already dead and mislabelling that as a clean retirement |

**C3 is the one that matters most and the one easiest to skip.** Spec 163 built
that pin last week. Retiring it is correct only because its *subject* goes in
the same commit; if the pin had already been vacuous, that would be a separate
defect in spec 163's work and would need saying. Run it, or the retirement is
an assumption.

### 5.3 Characterisation — captured green before, unmodified green after

| Suite | Command | Covers |
|---|---|---|
| Solution build | `dotnet build SmartSentinelEye.slnx -c Release` | The `.slnx` edit and the project removal |
| Architecture.Tests | `dotnet test tests/Architecture.Tests` | `AgentBriefClaimTests` (14 facts), `BoundaryTests`, `EndpointScopeDeclarationTests` |
| LayoutComposition unit tests | `dotnet test tests/LayoutComposition.*.Tests` | The four C# files whose doc comments change |
| Three JS workspaces | `npm run typecheck && npm run lint && npx vitest run` in `apps/shared`, `apps/kiosk-web`, `apps/management-web` | The TS deletions, the `exports` edit, the Vite config edit |
| Kiosk e2e | `e2e/kiosk-live-updates.spec.ts`, `e2e/kiosk-reconciliation.spec.ts` | The hub still pushes; `/hubs` routing on the kiosk side is untouched |

**Not one assertion in any of these may be edited.** An assertion that has to
move is evidence that behaviour moved: block and report, do not adjust.

### 5.4 Observation — the one thing tests do not cover

Boot the Aspire stack. Open `management-web` and confirm it loads, authenticates
and lists layouts with its `/hubs` dev proxy removed. This is the only item in
the spec with a plausible (if small) way to break something a suite would not
catch, and it takes one minute.

### 5.5 The reader's test (the P1 story, checked directly)

Grep the repository for `ADR-0076` and read every surviving hit. Each must be
either (a) `docs/adr/0076-*.md` itself, now headed `Superseded by ADR-0152`,
(b) `docs/adr/0152-*.md`, which cites it by design, (c) another **ADR**, which
is a historical record and is never retro-edited, or (d) a `specs/` artefact,
same reason. **No live document, comment, brief or config may be in that list.**

---

## 6. Out of scope, with reasons

| Item | Why not here |
|---|---|
| **A SignalR backplane** | ADR-0152 §4: "File it; do not fix it." The in-process `IHubContext` fan-out with a single-instance `layout-composition` silently drops frames for clients on a second replica. A backplane is its own piece of work. **Filed as an issue by T012.** |
| **Removing management-web's `.WithReference(layoutComposition)` / `VITE_LAYOUT_HUB_ORIGIN` / `.WaitFor(layoutComposition)` from `AppHost.cs:571-581`** | This is the mirror image of the dead Vite proxy and it *is* dead — but removing a `WaitFor` **changes the dev stack's startup graph**, and ADR-0152 says in terms that nothing changes at runtime. It is the one cleanup in reach that would make this spec's own claim false. **Filed as an issue by T013.** |
| **Editing ADRs 0074, 0075, 0106, 0107, 0111, 0112 where they cite ADR-0076** | ADRs are historical records. Supersession is recorded in the superseded document's header (T008), not by retro-editing every citing decision. ADR-0112's "SignalR v1" header is the drift-in-passing issue #2400 already noted; it stays as evidence. |
| **Editing `specs/162/` and `specs/003/`** | Same reason: a spec records what was believed when written. Spec 162 §4's deferred option (B) is *unblocked* by this work, not rewritten. |
| **A guard that fails when a solution project has no referrer** | It would have caught this in month one, and nothing today can. But it is **new behaviour**, ADR-0152 did not ask for it, and this spec's declared colour is behaviour-preserving throughout. Worth proposing separately; not smuggled in here. |
| **Observing SignalR's SSE fallback** | ADR-0152 §Decision says the fallback claim is inferred from configuration and that nobody has watched it happen. It explicitly declines to rely on it. Nothing here does either. |

---

## 7. Declarations

### 7.1 Which engineer — **frontend-engineer**, one agent

The change spans `src/`, `apps/`, `tests/`, `docs/` and the solution file, which
looks like it wants two engineers. It does not, and the reason is where the
*risk* sits rather than where the files sit.

- The only item needing real judgement is the **TypeScript** one: the
  `exports`-map edit, the two-files-one-commit ordering constraint around the
  type pin, and three workspaces to typecheck. That is frontend work.
- The **.NET** item is build plumbing with no C# logic at all — delete a folder,
  delete two `.slnx` lines, and reword four XML doc comments. No Domain,
  Application, Infrastructure or Api code changes. It needs `dotnet build` plus
  `dotnet test tests/Architecture.Tests` run, which is a command, not a
  speciality.
- The **Vite** config and its comment block are frontend config.

**A split would cost more than it buys.** `CLAUDE.md` and `CONTRIBUTING.md` are
touched by both halves, so the two sides would not own disjoint files and
ADR-0109's basis for `[P]` would not hold. Across ~15 file edits the parallelism
is worth nothing and the conflict is worth something.

**The condition that would change this answer:** if the AppHost *wiring* had to
change (the T013 item, deliberately deferred), this would become infra-engineer
work or a genuine two-engineer split. It does not, because that item is out of
scope.

**Explicit instruction to the engineer:** this is not a JS-only task. The .NET
build and `tests/Architecture.Tests` must be run, and `AgentBriefClaimTests` is
the guard most likely to be surprised by it.

### 7.2 Is a new ADR needed — **no**

Every work item maps to ADR-0152 §2, §3, §4 or its Implementation Notes. The
four rows this spec adds beyond the investigation's table (§2 rows 9-12) are all
inside §3's stated class — "Documents asserting the latter are corrected" — and
none of them decides anything.

**The one thing that could have made this a BLOCK was checked and is clear.**
`ILayoutLifecycleBroadcaster`'s comment cites "constitution IX" as authority for
keeping v2 candidates swappable. If §IX listed the real-time transport, removing
the abstraction would need a constitution amendment, which the autonomous lane
may not do. It does not: **§IX's table has four rows — Rule engine,
Authorization, Camera adapter, Observability sink — and the real-time transport
is not among them.** The constitution mentions ADR-0076, `realtime`, `SignalR`
and a WebSocket *transport* nowhere at all. So the comment's §IX citation is
simply wrong, T009 removes it, and **no constitution amendment is required.**

The one item that *would* have been a decision ADR-0152 did not make — removing
management-web's AppHost references, which changes the dev startup graph against
ADR-0152's "nothing changes at runtime" — is out of scope by §6 rather than
resolved by guesswork.

### 7.3 Behaviour colour — **behaviour-preserving throughout; phase 4a is characterisation, observed green**

Per CLAUDE.md, ambiguity resolves to red. **This is not ambiguous**, and the
argument is per-item rather than by assertion: nothing in this spec adds a code
path, a branch, a message, an endpoint, a scope or a field. Every item deletes
something with zero references or rewrites prose.

| Work item | Colour | What phase 4a must produce |
|---|---|---|
| T003 `Realtime.Abstractions` removal | Preserving | Release solution build captured green before and after; the §5.1 assembly-name absence proof |
| T005 `index.ts` + `surface.pin.ts` + `exports` removal | Preserving | **C3 first** (pin observed live), then three workspaces' typecheck/lint/vitest green before and after |
| T006-T010 document + comment corrections | Preserving | `AgentBriefClaimTests` green before and after, **unmodified**; C1 and C2 |
| T011 Vite proxy removal | Preserving | management-web typecheck/lint/vitest green; plus the §5.4 boot observation, because a dev-server proxy is the one thing no suite exercises |
| T012-T013 issue filing | No code | n/a |

**Where I disagree with the framing I was handed.** "The guards that must stay
green unmodified are the evidence" is *half* the evidence and the weaker half.
A green suite after a deletion is compatible with having deleted something live
that nothing covered — which is the failure mode, not the success criterion.
The load-bearing evidence is the pair: the **absence proof** (§5.1, searched on
each shape's invariant half, because a path-spelling search under-counts) and
the **three counterfactuals** (§5.2), which show the guards are live rather than
silent. Green-before/green-after is the third leg, and it is the one that would
have passed anyway.

**C3 is the item I would add that the framing omits.** Spec 163 built
`surface.pin.ts` days ago. Retiring an instrument is only clean if the
instrument worked; if it was already vacuous, that is a defect in spec 163 and
must be reported, not quietly swept up in a deletion that looks identical from a
green build.

---

## 8. Locked tech choices touched

None. No dependency is added or removed. `@microsoft/signalr` 10.0.11 stays in
`apps/kiosk-web`. `SmartSentinelEye.Shared.Kernel` loses its only unused
referrer. No EF migration, no realm change, no scope change, no Wolverine
route, no `Shared.Contracts` message.

The **stack table's own row changes** — `Real-time push` moves from
"**Replaceable transport** (WebSocket v1, SSE v2 candidate) | 0076" to a row
describing transport negotiation within SignalR, citing 0152. That is the point
of the spec, not a side effect.

## 9. Latency-budget impact — **N/A, and the reason is worth one line**

The push hop sits on §IV's **`Event → overlay state` (≤ 200 ms)** leg, and
**this spec does not touch it.** `LayoutLifecycleHub`, `SignalRLayoutLifecycle`
`Broadcaster`, `layoutHub.ts` and every Application event handler keep byte-
identical bodies; only four XML doc comments change. No leg is affected and no
measurement is owed.

Recorded rather than omitted because ADR-0152 says something this spec must not
quietly inherit: **no instrument measures that leg in isolation.** The only
figures are 555 ms and 758 ms server-side end-to-end against a 5,000 ms
regression ceiling, from a test that says in its own source it cannot read the
budget. That gap is real, it is not this spec's to close, and a spec that
touches the realtime transport's *documents* must not be mistaken later for one
that measured its *path*.
