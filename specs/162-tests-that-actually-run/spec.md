# Spec 162 — tests that actually run

**Phase:** 1 (Specify) — ADR-0037
**Issue:** [#2397](https://github.com/smartsolutionslab/smart-sentinel-eye/issues/2397) · **Branch:** `fix/2397-tests-that-actually-run`
**Lane:** autonomous (ADR-0144) — `#2397` carries `agent:ready`. **Engineer:** `frontend-engineer` · **Reviewer:** `frontend-reviewer`
**Base:** `origin/develop` @ `7803e2d6`, cut fresh — **not stacked** (memory: *spec branches cut from HEAD*).
**ADRs:** **ADR-0076** (replaceable realtime transport — the subject under test),
ADR-0139 (rules that fail the build, not the review), ADR-0144 (the lane; phase 4a's
two colours), ADR-0052 (test stack), ADR-0053 (test naming), ADR-0074 (two apps +
`apps/shared`), ADR-0108 (Playwright e2e gate), ADR-0109 (`[P]` marking),
ADR-0037 (the phased workflow).
**Constitution:** §Testing (a new-behaviour test that was never seen failing does not
satisfy the phase-4 gate); §IV (latency budget — **N/A**, below).

**Latency budget (§IV): N/A.** No production runtime file changes. Nothing under
`src/*/Domain`, `src/*/Application`, `apps/*/src/features`, the streaming path or the
SignalR hub client is touched. The one production file in scope
(`apps/shared/src/realtime/index.ts`) is **types-only and erases to empty JavaScript**
— it cannot be on any leg because it does not exist at runtime. No cell of the §IV
table moves and no measurement is owed.

**New ADR required for the slice as scoped: no.** §5 argues it. One question — whether
ADR-0076's client abstraction should survive at all — **is** an ADR-level decision, and
it is **escalated, not taken** (§4). The slice is deliberately scoped so that it ships
either way.

---

## 1. Premise verification — every claim in #2397, checked against the code

Memory: *verify the issue premise before planning* — 11 of ~20 board issues were stale by
delivery. Heiko wrote #2397 and asked for it to be checked rather than trusted. Every
claim holds, and the investigation found two further facts the issue does not contain
(§1.7, §1.8), one of which changes the recommended fix.

### 1.1 The file is collected by nothing — confirmed empirically, not by reading the glob

Reading `apps/shared/vitest.config.ts:16` shows `include: ['src/**/*.test.ts',
'src/**/*.test.tsx']`, which is what the issue says. That is an **artefact read**, and
memory (*guards that read the design artefact*) says an artefact read proves the design
was written down, not that it holds. So vitest was asked directly:

```
$ cd apps/shared && npx vitest list --run | grep -c "client.spec"
0
```

30 files are collected. `src/realtime/client.spec.ts` is **not one of them**. Confirmed
by the tool, not by the config.

### 1.2 It has been inert since it was written — confirmed

`git log` dates `apps/shared/src/realtime/client.spec.ts` to `ebb7f3fe` (2026-08-30,
*"chore(web): scaffold management-web + kiosk-web Vite apps with shared workspace"*).
Two tests, never once executed, in a suite that has been green throughout.

### 1.3 Both assertions restate their own inputs — confirmed, and it is worse than "weak"

```ts
const message: RealtimeMessage = { type: 'variable.changed', /* … */ };
expect(message.type).toBe('variable.changed');          // line 12 restates line 7

const surface: (keyof RealtimeClient)[] = ['connect', 'subscribe', 'disconnect'];
expect(surface).toHaveLength(3);                        // line 17 counts line 16
```

The second is **unfalsifiable with respect to its stated subject**. Its name promises a
guard over "a connect/subscribe/disconnect surface that v1 WebSocket and v2 SSE both
implement". `RealtimeClient` could gain a fourth member or lose one and this assertion
would not move, because neither its input nor its expectation is derived from
`RealtimeClient` at runtime — `keyof` is erased before the test executes. The array and
the number 3 are both literals in the same file, three lines apart.

### 1.4 `tsc` already checks what the tests claim to check — and checks it better

Both tests import types from `./index.js`, and `apps/shared/tsconfig.json` has
`"include": ["src/**/*"]`, so `client.spec.ts` **is** typechecked by `pnpm typecheck`
even though it is never run. A throwaway probe (written, run, deleted; `git status`
clean afterwards) confirms `tsc` catches both defects the tests gesture at:

```
src/realtime/__tscprobe.ts(3,77): error TS2322: Type 'number' is not assignable to type 'string'.
src/realtime/__tscprobe.ts(5,89): error TS2820: Type '"reconnect"' is not assignable to
  type 'keyof RealtimeClient'. Did you mean '"connect"'?
```

TS2820 even names the intended member. The runtime assertions add nothing to this; they
are strictly dominated by a check that already runs on every CI build.

### 1.5 `client.spec.ts` is the only `*.spec.*` under any app's `src/` — confirmed

The issue flagged its own search as non-exhaustive. Repeated repo-wide:

```
$ find apps -path '*/src/*' -name '*.spec.*' -not -path '*/node_modules/*'
apps/shared/src/realtime/client.spec.ts          # the only one
```

The other **23** `*.spec.ts` files all live under `e2e/`. The population does not change
the answer — it sharpens it: the practice under `apps/*/src` is 100+ `.test.ts(x)` files
and exactly one `.spec.ts`, and that one is the dead file.

### 1.6 The two namespaces are separated by **directory**, not by suffix

`playwright.config.ts:9` sets `testDir: './e2e'`. Playwright never looks inside
`apps/*/src` at all. So "`.spec` means Playwright" is a **convention observed in
practice**, not a mechanism — nothing would break if a Playwright file were named
`.test.ts`, and nothing routed `client.spec.ts` to Playwright. It simply went nowhere.

### 1.7 **Not in the issue:** the same filename *would* run in the other two apps

`apps/shared` is the **only** package in the repo with a `vitest.config.ts`.
`apps/kiosk-web` and `apps/management-web` both run `"test": "vitest run"` with **no
config**, so they inherit vitest's default include —
`**/*.{test,spec}.?(c|m)[jt]s?(x)` — which **does** match `.spec.ts`.

So the repo is inconsistent in the failure-silent direction: an identical file under
`kiosk-web/src` would have run all along. Only `apps/shared` narrowed the glob, and
narrowing a collection glob fails silently by construction. This is why the fix cannot be
a naming rule (§5).

### 1.8 **Not in the issue, and it changes the design question:** nothing implements `RealtimeClient`

This is the decisive finding. `RealtimeClient`, `RealtimeMessage` and
`RealtimeSubscription` are referenced **only** by their own declaration in
`apps/shared/src/realtime/index.ts` and by the dead `client.spec.ts`. Searched twice, the
second time on the invariant half of the shape (memory: *grep the invariant half*, *search
twice before claiming absence*):

| Symbol | Declared | Implemented | Consumed |
|---|---|---|---|
| `RealtimeClient` | `realtime/index.ts:16` | **0** | **0** |
| `RealtimeMessage` | `realtime/index.ts:5` | **0** | **0** (outside its own interface) |
| `RealtimeSubscription` | `realtime/index.ts:12` | **0** | **0** (outside its own interface) |

Every real consumer imports the **concrete SignalR client** instead:

```
apps/kiosk-web/src/features/revocation/useLayoutLifecycle.ts:15
  from '@smart-sentinel-eye/shared/realtime/layoutHub'
```

Seven import sites, all `shared/realtime/layoutHub`. **Zero** import `shared/realtime`.
`layoutHub.ts` exposes `start` / `stop` / `state` (`LayoutHubHandle`) — it does **not**
satisfy `RealtimeClient`'s `connect` / `subscribe` / `disconnect`, and never claimed to.

The server side matches: `src/Realtime.Abstractions/` contains **zero `.cs` files** — an
empty `csproj` listed in `SmartSentinelEye.slnx` — and `IRealtimeChannel` /
`IRealtimeTransport` have **zero** references anywhere in `src/`.

ADR-0112's header line already records the divergence in passing: *"ADR-0076 (replaceable
real-time transport; **SignalR v1**)"* — whereas ADR-0076 itself specifies v1 as **native
WebSocket** and lists SignalR under *Alternatives Considered* as rejected ("heavier and
couples to the framework's protocol envelope").

**So `apps/shared/src/realtime/index.ts` is unadopted scaffolding, and the two dead tests
are its only readers.** That is the fact that decides §3.

---

## 2. What this spec is, and what it is not

**It is not a rename.** Renaming `client.spec.ts` → `client.test.ts` makes two
unfalsifiable assertions execute and pass, converting an invisible non-test into a
visible non-test. The suite count goes up by two and the protection goes up by nothing.
#2397 says this and it is right.

**It is not a fix to ADR-0076's adoption gap.** §1.8 is a real architectural divergence.
Closing it means either implementing the abstraction or retiring it, and both are
decisions the lane may not make (§4).

**It is:** the instrument problem. Two defects, in increasing order of value —

1. a test file no runner collects (invisible by construction);
2. an assertion that cannot fail (visible, and therefore worse — it reports green);
3. the absence of anything that would catch either again.

(3) is the deliverable with a life beyond this file. (1) and (2) are its first red.

---

## 3. Question 1, answered — what should these tests assert?

**Answer: nothing, in vitest. The honest home for this claim is the type system, and the
honest home for the *recurrence* is a collection guard.**

Heiko asked for this to be argued rather than asserted, and named it a legitimate outcome
rather than a cop-out. The argument is in four steps, two of them measured.

### 3.1 A runtime test over this module is a category error

`apps/shared/src/realtime/index.ts` contains one `interface` and two `type` aliases and
nothing else. It **erases completely**; the emitted JavaScript is empty. A vitest test
imports a module with no runtime, constructs a literal, and asserts about the literal.
There is no subject. This is not "a weak runtime test" — it is a runtime test of
something that has no runtime.

### 3.2 A contract test is not available, because there is nothing to run it against

The strongest option #2397 lists — "a contract test run against the real
implementation(s)" — would indeed be worth far more than either current assertion. It is
**impossible here**: §1.8 measured **zero** implementations of `RealtimeClient`. A
contract test needs at least one implementor to instantiate; there are none, in either
language.

The tempting substitute — assert that `layoutHub.ts` satisfies `RealtimeClient` — would
be **red and unfixable within this slice**, because it does not and was never built to
(`start`/`stop`/`state` vs `connect`/`subscribe`/`disconnect`). Writing that test would be
specifying net-new behaviour under the banner of a bug fix, and it runs straight into §4.

### 3.3 A type-level assertion *can* fail, and catches the exact regression the test name promises

`tsc` already catches a key that is **not** on `RealtimeClient` (§1.4, TS2820). It does
**not** catch a member being **added** — `(keyof RealtimeClient)[]` is satisfied by any
subset, so a fourth member slips through. #2397 names precisely this gap, and it is real.

A type-level exhaustiveness pin closes it in **both** directions. Probe, run and reverted:

```ts
type Exact<A, B> = [A] extends [B] ? ([B] extends [A] ? true : false) : false;

interface Grew   { connect(): void; subscribe(): void; disconnect(): void; reconnect(): void }
interface Shrank { connect(): void; subscribe(): void }

export const grew:   Exact<keyof Grew,   'connect'|'subscribe'|'disconnect'> = true;
export const shrank: Exact<keyof Shrank, 'connect'|'subscribe'|'disconnect'> = true;
```

```
src/realtime/__probe2.ts(9,14):  error TS2322: Type 'true' is not assignable to type 'false'.
src/realtime/__probe2.ts(13,14): error TS2322: Type 'true' is not assignable to type 'false'.
```

A gained member and a lost member each fail the build, and the baseline (current shape)
compiles clean. **This is the assertion the second test's name has been claiming to make
since 2026-08-30.** It belongs in a `.ts` file read by `pnpm typecheck`, not in a vitest
`it()`.

### 3.4 The discrimination criterion — how a reviewer tells the difference

Heiko asked for this explicitly, and it is the generalisable part. The test that separates
a real assertion from a restated input:

> **Can the assertion's subject change without the assertion's text changing?**
> The expectation and the subject must live in **different files**, and the counterfactual
> must be constructed in the **subject's** file.

Applied:

| Assertion | Subject lives in | Expectation lives in | Discriminates? |
|---|---|---|---|
| `expect(message.type).toBe('variable.changed')` | `client.spec.ts:7` | `client.spec.ts:12` | **No** — same file, 5 lines apart |
| `expect(surface).toHaveLength(3)` | `client.spec.ts:16` | `client.spec.ts:17` | **No** — adjacent lines |
| `Exact<keyof RealtimeClient, …>` pin | `realtime/index.ts` | the pin file | **Yes** — proven, §3.3 |
| Collection guard | vitest's own resolver | `scripts/…test.mjs` | **Yes** — the tool computes it |

This is the same species as spec 161's rule (*"Test 1 alone is satisfied by `selector:
"*"`"*), and it is why phase 4a's red for US2 must be **constructed in `index.ts`**, not
in the pin (§8).

### 3.5 What happens to the two existing tests

They are **deleted, not renamed and not rewritten in place.** This needs saying plainly
because "delete a test" is the shape of the thing the lane may not do (ADR-0144: weaken a
gate to reach green).

It is not that, and the distinction is measurable rather than rhetorical: **these tests
have never executed** (§1.1, confirmed by asking vitest). Deleting them removes exactly
zero assertions from the suite, changes zero counts, and cannot change any result. The
coverage they contribute is provably nil. What replaces them (§3.3) can fail, which is
strictly more gate than the repo has today.

**The reviewer's check on this claim:** `pnpm test` in `apps/shared` must report the
**same total** before and after the deletion. If the number moves, the premise was wrong
and the deletion is a block.

---

## 4. `[DECISION REQUIRED]` — the human gate at phase 3

**ADR-0144 forbids the lane from deciding this. It is surfaced, not taken.**

§1.8 establishes that ADR-0076's client abstraction was never adopted: zero implementors,
zero consumers, an empty server-side `csproj`, and a shipped transport (SignalR) that the
ADR itself lists as rejected. Two coherent futures follow, and the choice between them is
an architecture decision:

> **The question for the human, answerable in one line:**
>
> **(A)** **Keep the abstraction.** `RealtimeClient` stays; the slice replaces its dead
> runtime tests with the type-level pin (§3.3) and adds the collection guard. The pin
> asserts something modest but real: if someone changes the interface's shape, they are
> made to notice they have moved ADR-0076's "v2 drops in without changing consumers"
> promise.
>
> **(B)** **Retire the abstraction.** Delete `apps/shared/src/realtime/index.ts`, its
> `"./realtime"` entry in `apps/shared/package.json`, the two dead tests, and the empty
> `src/Realtime.Abstractions/` project + its `slnx` entry. Keep only the collection guard.
> This is the honest engineering answer to "nothing implements it" — but it **removes an
> artefact ADR-0076 names by path**, which is an ADR-level act.

**The architect's recommendation is (A), and it is a recommendation about *sequencing*,
not about which is right.** (B) is probably where this ends up; §1.8 is a strong finding
and the scaffolding has earned nothing in fifteen months. But (B) cannot ship from this
issue: retiring a named ADR artefact requires either superseding ADR-0076 or recording
that its client half is abandoned, and **the lane may not write an ADR**. Choosing (B)
here is therefore a **BLOCK** on #2397 pending that ADR — not a judgement call for phase 4.

Choosing (A) leaves the (B) question open and better documented than it is today, and
costs one small file that a later deletion removes in the same breath as `index.ts`.

**Everything downstream is written for (A).** If the human answers **(B)**: US2 changes
from "replace the assertion" to "delete the module", T005–T007 collapse into one deletion
task, the counterfactual T004 is dropped, and **US1 and its guard are unaffected** — which
is the point of scoping the slice this way. Nothing else moves.

**A third answer — "leave `RealtimeClient` alone entirely, ship only the guard" — is also
coherent**, and produces a smaller (A). It is not recommended, because the guard would
then force the dead file's resolution without anyone having decided what it should assert,
which is the question the issue was filed to settle.

---

## 5. Questions 3 and 4, answered — the suffix convention, and what stops this recurring

### 5.1 The convention is real, but it is not the defect worth fixing

The practice is unambiguous (§1.5): 23/23 under `e2e/` are `.spec`, 100+ under
`apps/*/src` are `.test`, and the single exception is the dead file. So **the defect is
the filename, not the glob** — as #2397 suspected.

**But do not fix it by widening `apps/shared`'s include to match `*.spec.ts`.** Widening
makes the three apps consistent in the wrong direction: it admits a suffix the repo does
not use under `src/` and that Playwright owns elsewhere, and it fixes exactly one file
while leaving the failure mode — *a test file that no runner collects* — fully intact for
every other spelling (`client.tests.ts`, `client.test.mts`, a file under a directory the
glob misses).

### 5.2 The mechanism should be **reachability**, not naming

A naming rule answers "is this file called the right thing?". The defect is "does anything
run this file?". Those come apart, and §1.7 shows they already have: the same filename is
collected in two apps and not in the third, so no single naming rule is even true
repo-wide.

So the guard asserts the thing that actually went wrong: **every test-shaped file under an
app's `src/` is in the set of files that app's vitest actually collects.** It is neutral
on the suffix — renaming `client.spec.ts` to `.test.ts` satisfies it, and so does deleting
it — which is correct, because the naming question is the weaker one and §4 owns the
stronger one.

Crucially, and by direct precedent, the guard **asks vitest** rather than reading
`vitest.config.ts`. `scripts/lint-scope.test.mjs` already draws exactly this distinction
in its own header comment:

> *"`every e2e file resolves a TypeScript rule set` asks ESLint itself, so it covers a file
> added tomorrow that no config block happens to match. The `package.json` / `ci.yml`
> assertions read an artefact."*

A guard that parses the include glob would be green against a config that was correct and
a resolver that ignored it. Memory: *guards that read the design artefact* prove the design
was written down, not that it holds.

### 5.3 Does the guard need an ADR? **No** — and the line is precedented, not invented

The repo has two `node --test` config guards and they sit on opposite sides of the line:

| Guard | Cites | Why |
|---|---|---|
| `scripts/settle-rule.test.mjs` | **ADR-0150** | Enforces a **new cross-cutting prohibition** on how test code may be written. Needed a decision first. |
| `scripts/lint-scope.test.mjs` | `#2219 / spec 114`, **no ADR** | Asserts that an **existing gate actually covers what it claims**. Created no new rule. |

This guard is the `lint-scope` species. It introduces no convention, prohibits no idiom,
and constrains nobody's code style. It asserts that `pnpm test` — a gate that already
exists and already runs in CI — collects the test files that exist. Nothing is decided;
something already decided is made checkable.

The harness needs no wiring either: `test:guards` (`node --test "scripts/**/*.test.mjs"`)
is already part of the root `test` script, and `.github/workflows/ci.yml:126` already runs
`pnpm test` in the `frontend` job. **`ci.yml` is not modified by this spec.**

---

## 6. User stories, prioritised

### US1 (P1) — a test file that no runner collects fails the build

**As** an engineer adding a test to a frontend app, **when** I name it something the app's
vitest config does not collect, **I want** `pnpm test` to fail and name my file, **so
that** I find out in the minute I wrote it rather than fifteen months later by accident
during someone else's phase-5 verification.

Independently shippable: **yes** — one guard script, one PR, observable end to end by
running `pnpm test:guards` (§9). Independently valuable: **yes**, and it is the only part
of this slice with a life beyond `client.spec.ts`. It is also the **forcing function**:
once it exists, the dead file must be resolved one way or the other to get to green.

### US2 (P2) — the realtime surface assertion can actually fail

**As** the engineer who one day implements ADR-0076's v2 transport, **when** someone
changes `RealtimeClient`'s shape, **I want** the build to fail, **so that** the
"v2 drops in without changing consumers" promise is checked by something rather than
asserted by a test that counts its own array literal.

Independently shippable: **yes**, and separable from US1 — it is one file plus one
deletion. Depends on §4 answering **(A)**; under **(B)** this story is replaced by a
deletion and the slice still ships.

### US3 (P3) — the ADR-0076 adoption gap is written down rather than rediscovered

**As** the next person to open `apps/shared/src/realtime/`, **I want** the fact that
nothing implements this interface to be recorded where I will see it, **so that** I do not
spend an afternoon working out why the "replaceable transport" has no replaceable anything
behind it.

Shippable as a comment in the surviving file. **No ADR is written** (§4). Under (B) this
story disappears with the module.

---

## 7. Acceptance scenarios (Gherkin)

### 7.1 Happy path — the guard reports the defect that exists today

```gherkin
Scenario: the uncollected file is named by the guard
  Given apps/shared/src/realtime/client.spec.ts exists
    And apps/shared/vitest.config.ts includes only 'src/**/*.test.ts' and '.test.tsx'
  When `pnpm test:guards` runs
  Then the collection guard fails
   And the failure message names `apps/shared/src/realtime/client.spec.ts` by path
   And the process exits non-zero
```

```gherkin
Scenario: resolving the file turns the guard green, by either route
  Given the guard is failing on client.spec.ts
  When the file is deleted
   Or the file is renamed so the app's vitest collects it
  Then `pnpm test:guards` passes          # the guard is neutral on WHICH route
```

### 7.2 Conflict — the guard must not flag what is sound

```gherkin
Scenario: the 30 collected files are not reported
  Given apps/shared collects 30 test files
  When the collection guard runs
  Then it reports zero problems for apps/shared beyond client.spec.ts
   And it reports zero problems for apps/kiosk-web
   And it reports zero problems for apps/management-web
   # kiosk-web and management-web have no vitest.config.ts, so their default
   # include already matches BOTH suffixes (§1.7). A guard that flagged them
   # would be a naming rule wearing a reachability rule's clothes.
```

```gherkin
Scenario: a Playwright spec is not a frontend-app test
  Given e2e/cameras.spec.ts exists and is collected by Playwright, not vitest
  When the collection guard runs
  Then e2e/ is not examined at all       # scoped to apps/*/src; testDir is './e2e'
```

### 7.3 Bad request — the guard must not be satisfiable by matching everything

```gherkin
Scenario: the guard discriminates rather than always-failing or always-passing
  Given a scratch file apps/shared/src/realtime/scratch.spec.ts containing one test
  When `pnpm test:guards` runs
  Then the guard fails and names scratch.spec.ts
  When the same file is renamed to scratch.test.ts
  Then `pnpm test:guards` passes
  # Both halves are required. The first alone is satisfied by a guard that
  # flags every file; the second alone by a guard that flags none.
```

### 7.4 Auth / scope — reinterpreted, not skipped

There is no token, scope or fab boundary anywhere in this change; nothing here reads,
mints or validates a credential. The analogue that **does** apply is **guard scope** — the
blast radius of a rule that fails the build:

```gherkin
Scenario: the guard's scope is the three app packages and nothing else
  When the collection guard enumerates candidate roots
  Then it covers exactly apps/shared, apps/kiosk-web and apps/management-web
   And it does not examine e2e/, scripts/, src/ (the .NET tree) or node_modules
   And a fourth app added under apps/ with a package.json "test" script is covered
       automatically rather than needing the guard edited
```

The last clause is the one that matters and the one most likely to be cut: a guard that
hardcodes three paths goes stale the day a fourth app appears, and goes stale **silently**
— which is the exact failure mode this whole spec exists to remove.

### 7.5 US2 — the type-level pin

```gherkin
Scenario: the current shape compiles
  Given RealtimeClient declares connect, subscribe and disconnect
  When `pnpm typecheck` runs in apps/shared
  Then it exits zero

Scenario: a gained member fails the build
  Given a fourth member `reconnect` is added to RealtimeClient
  When `pnpm typecheck` runs
  Then tsc reports TS2322 at the pin
   And the error points at realtime/index.ts's consumer, not at a test file

Scenario: a lost member fails the build
  Given `disconnect` is removed from RealtimeClient
  When `pnpm typecheck` runs
  Then tsc reports TS2322 at the pin
  # Both counterfactuals are mandatory. The pin is GREEN the moment it is
  # written, so green proves nothing on its own (§8).
```

---

## 8. Phase 4a colour declaration (constitution §Testing, ADR-0139, ADR-0144)

**BEHAVIOUR-CHANGING. Every phase-4a artefact is observed RED first.** Stated positively
rather than by omission, because ambiguity resolves to red and a silent declaration is how
that gets missed.

### 8.1 The awkwardness, addressed head-on

The artifact under change **is itself a test**, and the "new behaviour" is *that a test can
fail at all*. That makes the usual red/green vocabulary slippery in two specific ways, and
both are handled explicitly rather than waved past:

**(a) Deleting a test looks like weakening a gate.** It is not, here, and the claim is
checkable rather than argued: the deleted tests have never executed (§1.1). The reviewer's
discharge is a **count comparison** — `pnpm test` in `apps/shared` reports the same total
before and after. If it moves, the premise was wrong and the deletion is a block.

**(b) The new assertions are green the moment they are written.** The type-level pin
compiles clean against the current shape; that is what "the current shape is correct"
means. So green is not evidence of anything — a pin over `Exact<never, never>` would also
be green. This is the same trap spec 161 names, and the answer is the same: **the red must
be constructed in the subject's file** and quoted.

### 8.2 What "observed red" means concretely

| Story | Artefact | The red that must be observed and quoted verbatim in the PR |
|---|---|---|
| US1 | `scripts/<collection-guard>.test.mjs` | Run **before** `client.spec.ts` is touched. The guard must fail naming `apps/shared/src/realtime/client.spec.ts`. **The guard's first red is the bug itself** — no fixture needed for this half. |
| US1 | discrimination | A scratch `*.spec.ts` under `apps/shared/src` makes the guard name **two** files; renaming it to `.test.ts` returns the guard to naming **one**. Both halves quoted. Scratch file deleted before commit; `git status` clean. |
| US2 | the type-level pin | Green on write. Red constructed **in `apps/shared/src/realtime/index.ts`**: add `reconnect(): void` → `pnpm typecheck` reports `TS2322: Type 'true' is not assignable to type 'false'`. Revert. Then remove `disconnect()` → same error. Revert. Both quoted. |

`test-writer` produces the guard and returns the **verbatim** output. The engineer receives
that output as its brief and **may not edit the guard or the pin to reach green**
(ADR-0144).

### 8.3 How the reviewer tells the new assertions discriminate

Three checks, in order of strength, and the first is the one to insist on:

1. **The counterfactuals were constructed in the subject's file, not in the assertion's.**
   The `reconnect` member goes into `index.ts`; the scratch `.spec.ts` goes into
   `apps/shared/src`. An assertion whose counterfactual can only be built by editing the
   assertion is the defect this spec is fixing (§3.4).
2. **Both directions are present.** A guard proven only by "it fails on the bad file" is
   satisfied by a guard that fails on everything — spec 161's `selector: "*"` point. The
   must-not-flag half is not optional.
3. **The subject and the expectation live in different files.** Mechanical, greppable, and
   the one-line rule from §3.4 that a reviewer can apply to the next test as well as this
   one.

**Nothing in this spec is behaviour-preserving**, so no characterisation obligation arises
and none should be invented. The single deletion (§3.5) is not a refactor — it removes
code that provably never ran, and its discharge is the count comparison in §8.1(a), not a
characterisation suite.

---

## 9. Independent end-to-end test procedure (phase 5)

Runnable by a reviewer with no knowledge of the implementation, start to finish, without
reading any artefact this spec produced.

```sh
# 1. Establish the defect exists on develop. Ask vitest, do not read the config.
git checkout origin/develop
cd apps/shared && npx vitest list --run | grep -c "client.spec"; cd ../..
# EXPECT: 0  — the file exists on disk but is collected by nothing.
ls apps/shared/src/realtime/client.spec.ts
# EXPECT: the file is there. Two tests that have never run.

# 2. The gate is currently, wrongly, green.
pnpm test && echo "GREEN ON DEVELOP"
# EXPECT: green. This is the whole complaint.

# 3. Switch to the branch. The guard now reports the defect.
git checkout fix/2397-tests-that-actually-run
git stash list   # expect empty; nothing below relies on stashed state
node --test "scripts/**/*.test.mjs"
# EXPECT: green NOW (the file was resolved as part of the fix). To see the red
#         the guard was born with, restore the defect — step 4.

# 4. THE DISCRIMINATION PROOF. Reintroduce an uncollected file and watch it go red.
cat > apps/shared/src/realtime/scratch.spec.ts <<'EOF'
import { describe, it, expect } from 'vitest';
describe('scratch', () => { it('runs', () => { expect(1).toBe(1); }); });
EOF
node --test "scripts/**/*.test.mjs"
# EXPECT: non-zero exit; the failure names apps/shared/src/realtime/scratch.spec.ts.

# 5. The other half — rename it and the guard goes green. Without this step,
#    step 4 is also satisfied by a guard that flags every file it sees.
mv apps/shared/src/realtime/scratch.spec.ts apps/shared/src/realtime/scratch.test.ts
node --test "scripts/**/*.test.mjs"
# EXPECT: green, AND `cd apps/shared && npx vitest list --run | grep scratch`
#         now shows the file — it is genuinely collected, not merely tolerated.
rm apps/shared/src/realtime/scratch.test.ts
git status --porcelain   # EXPECT: empty. Nothing from steps 4-5 survives.

# 6. US2 — the pin fails on a shape change. Edit the SUBJECT, not the assertion.
#    Add `reconnect(): void;` to RealtimeClient in apps/shared/src/realtime/index.ts,
#    then:
cd apps/shared && npx tsc --noEmit; cd ..
# EXPECT: non-zero exit, TS2322 "Type 'true' is not assignable to type 'false'".
#    Revert index.ts. Then DELETE `disconnect(): void;` and repeat.
# EXPECT: the same TS2322. Revert.
git status --porcelain   # EXPECT: empty.

# 7. The deletion removed no coverage — the count must be unchanged.
git checkout origin/develop && cd apps/shared && npx vitest run 2>&1 | tail -5; cd ../..
git checkout fix/2397-tests-that-actually-run && cd apps/shared && npx vitest run 2>&1 | tail -5; cd ../..
# EXPECT: the SAME number of passing tests on both. If the branch reports fewer,
#         the deleted tests were running after all and §3.5's premise was wrong.
```

**The step most likely to be skipped is 5**, and it is the one that makes 4 mean anything.
The step most likely to be *faked* is 6, by editing the pin instead of `index.ts` — a
reviewer should confirm from the diff of the probe that `index.ts` was what changed.

---

## 10. Locked tech choices (no new dependency)

| Concern | Choice | Why |
|---|---|---|
| Guard harness | `node --test` over `scripts/**/*.test.mjs` | **Existing pattern** — `scripts/lint-scope.test.mjs` and `scripts/settle-rule.test.mjs` already do this. Already wired: root `test:guards` → root `test` → `ci.yml:126` |
| How the guard learns what is collected | **Ask vitest** (its CLI/Node API), per app, against the app's own shipped config | `lint-scope`'s stated distinction: asking the tool covers a file added tomorrow; reading the config proves only that the config was written |
| Type-level assertion | Plain TypeScript `Exact<A,B>` conditional pin in a `.ts` file, read by `pnpm typecheck` | **No new dependency and no config change.** `vitest expectTypeOf` was considered and rejected — it needs `test.typecheck` enabled in `vitest.config.ts`, which is a new runner mode to maintain for one assertion |
| Test stack | unchanged — vitest (ADR-0052) | Nothing is added to any `package.json` |
| CI | **unchanged** | `frontend` already runs `pnpm lint`, `pnpm typecheck` and `pnpm test` (`ci.yml:120/123/126`). **`ci.yml` is not modified.** |

**Nothing is added to any `package.json`.**

---

## 11. Success criteria

- **SC-1** `pnpm test:guards` fails on `origin/develop`'s tree (with `client.spec.ts`
  restored) naming that file, and passes on the branch. Both observed, both quoted.
- **SC-2** The guard's must-not-flag half is demonstrated: a renamed scratch file returns
  it to green, and vitest confirms the renamed file is genuinely collected.
- **SC-3** The guard covers a hypothetical fourth app without being edited (§7.4).
- **SC-4** `pnpm typecheck` fails on a gained member and on a lost member of
  `RealtimeClient`, with the probe applied to `index.ts`. Both quoted.
- **SC-5** `apps/shared`'s passing-test count is **identical** before and after (§3.5).
- **SC-6** `pnpm lint`, `pnpm typecheck`, `pnpm test` all green on the branch.
- **SC-7** `git status` clean after every probe in §9; no scratch file reaches the index.

## 12. Out of scope, named

- **Implementing ADR-0076's client abstraction.** Net-new behaviour, and §4's decision
  gates it.
- **Retiring ADR-0076's client abstraction**, including `src/Realtime.Abstractions/` and
  the `slnx` entry — that is §4 option (B) and needs an ADR.
- **Reconciling ADR-0076 (v1 = native WebSocket, SignalR rejected) with the shipped
  SignalR transport and ADR-0112's "SignalR v1" aside.** A genuine ADR-level divergence,
  found here, recorded here, **not fixed here.** Worth its own issue.
- **Widening `apps/shared`'s vitest include to `*.spec.ts`** — argued against, §5.1.
- **Adding `vitest.config.ts` to `kiosk-web` / `management-web`** to make the three apps
  consistent. Defensible, but it is a second change with its own risk (those apps
  currently collect by default and a hand-written include could *create* the very gap this
  guard exists to catch). The guard makes the inconsistency harmless; leave it.
- **A repo-wide naming rule for test files.** §5.2: reachability is the property that
  failed, and a naming rule would need a convention decision the lane may not take.
- **Backfilling guards for other silent-collection surfaces** (Playwright's `testMatch`
  projects, the .NET test discovery). Same species, different tools; file separately if
  wanted.

## 13. Assumptions, marked

- **A-1** `vitest list --run` reflects the same resolution as `vitest run`. Verified for
  `apps/shared` (30 files listed, matching the suites that run). **If the guard's chosen
  API disagrees with `vitest run` for any app, stop and report** — a guard that asks a
  different question than the gate is worse than none.
- **A-2** "Test-shaped file under `src/`" is taken to mean a file whose name contains
  `.test.` or `.spec.` before a `ts`/`tsx`/`mts`/`cts`/`js`/`jsx` extension. This is a
  *heuristic for the candidate set*, and it is the guard's one soft edge: a test file named
  `clientChecks.ts` is invisible to it. Recorded rather than hidden — the guard closes the
  spellings the repo actually uses and the one it actually got wrong, not every conceivable
  one.
- **A-3** §4 is answered **(A)**. Everything downstream is written for it; (B)'s reduction
  is spelled out in §4 and in `tasks.md`'s gate note.
- **A-4** The two dead tests contribute zero coverage. Measured (§1.1), and re-checked at
  phase 5 by SC-5 rather than assumed.
- **A-5** No `[NEEDS CLARIFICATION]` remains. §4 is a decision *gate*, not an unresolved
  ambiguity: both branches are fully specified and the slice ships under either.
