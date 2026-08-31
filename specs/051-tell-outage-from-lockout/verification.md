# Verification — 051 tell an outage from a lockout

Phase 5.

---

## 0. What shipped

A screen that cannot renew its grant now does what the *cause* deserves. An
absent identity service is waited out, unattended. A screen the provider has
refused says so, and shows nothing anyone can type a password into.

**The feature that was filed is not the feature that was built.** Issue 1990
said two failures rendered as one merged screen; induced against a running
provider they rendered as three different ones, and the real defect was the
opposite of the one reported — the failure that resolves by itself was the one
that never retried. The issue was corrected in the open.

---

## 1. Automated checks

| Check | Result |
|---|---|
| `pnpm format:check` | pass |
| lint / typecheck across the app packages | pass |
| Unit tests | **114 pass** in `kiosk-web` (471 across all app packages) |
| Playwright, `kiosk` project | **13 pass, 7 skipped** — the seven are the specs spec 050 gated |
| Backend | no C# changed; **no coverage gate applies and none is cited** |

Read on exit codes, and the checks are gated on `$?` rather than sharing a
command with the commit. That distinction is here because it was got wrong once
during this feature: a commit landed on a red lint because both ran in one line.

---

## 2. Demonstrated against a real provider

**Stopped, and an account disabled. Not stubbed.** Route interception is what
runs in CI; it is not what proves this.

| Claim | Result |
|---|---|
| An absent provider shows a reconnecting screen, not a dead one | **pass** |
| It retries with nobody touching it | **pass** — attempts climb before anything is pressed |
| The wall returns when the provider recovers, untouched | **pass — about 34 s after the restart command**, of which ~20 s is the provider becoming able to serve |
| A disabled account says the screen is not authorized | **pass** |
| That screen carries no credential field | **pass — zero `input` elements on the page** |
| It is not handed to the provider's pages | **pass** — still on the kiosk's origin |
| A refused screen stops asking | **pass** — request count flat over 30 s |
| Several screens do not arrive together | **pass** — four screens, attempts measurably spread |
| The ceiling drop-out still behaves as before | **pass** |

**Before, measured the same way:** the provider was stopped, started, and left
alone — and ninety seconds later the screen still read *"Sign-in failed / Failed
to fetch"*. A disabled account rendered the provider's own login form.

---

## 3. What was *not* demonstrated

- **A wall over days.** Every check watches one screen for seconds or minutes.
  Unattended operation is a property of twenty screens over a shift.
- **Twenty screens.** Four were exercised. What was shown is the *spread*, not
  the scale, and the constitution's number is twenty.
- **A real network partition.** An aborted request is not DNS failure, not TLS
  failure, and not a connection that hangs. The hang path — `ErrorTimeout` —
  exists in the rule and is covered only by a unit case.
- **A provider under genuine load.** `server_error` was stubbed. That is a stub
  of the response, not of the load, and it is the case the whole rule is shaped
  around.
- **Anything in production.** There is none (ADR-0130).

**This feature does not close §Availability.** The ten-hour ceiling still drops
a screen roughly twice a day (issue 1989, blocked on issue 1992) and is the more
frequent failure.

---

## 4. Four defects the tests found only by running, and two the mutations found

**Every unit test passed while three of these were live.** That is the finding.

| # | What was wrong | Why no unit test saw it |
|---|---|---|
| 1 | The classifier read `auth.error.error`, which is **always undefined** — the binding normalises the error and keeps the original under `innerError` | every test handed it an *unwrapped* error, which does carry the code |
| 2 | `signinSilent` **does not reject**; it resolves `null` and sets the error a commit later | the doubles rejected |
| 3 | The token-expired event, which carries no cause, pre-empted the classifier and redirected | the race needs a real load sequence |
| 4 | A **stale closure** redirected a screen already ruled refused, 0.2 s after the verdict | the callback is recreated per render; a hand-driven hook never defers long enough |

**Number one is the one that mattered.** With it live, every refusal classified
as recoverable — a shut-out screen would have retried forever, never saying it
was shut out, and US2 would have shipped as a screen that merely *looked* right.

**Number four showed nothing on screen.** The words were correct throughout;
only the network traffic and the address bar disagreed. It was found by a test
that counted requests rather than reading the page.

### And two mutations that escaped, both of them the test's fault

- **The jitter test derived its threshold from the constant under test**
  (`CEILING_MS * JITTER_FRACTION`), so shrinking jitter shrank the bar. A
  fraction of `0.0001` put twenty screens within three milliseconds of each
  other and left the suite green. The threshold is now an absolute five seconds,
  and a halved-but-sane `0.15` still passes — so it is calibrated, not merely
  tightened.
- **Nothing exercised an unclassified error**, so restoring the old screen that
  printed the library's own words left every test passing. That path *is* the
  asymmetric default in practice, and it was resting on nothing.

---

## 5. What the checks prove, and what they do not

| Claim | Proved by | Not proved by |
|---|---|---|
| A recoverable failure retries | attempts climbing, untouched | the screen reading "Reconnecting" |
| The wall returns unattended | a **real** provider stopped and started | the manual button, which already worked |
| A refused screen shows no prompt | counting `input` elements at zero | a better heading |
| An overloaded provider is recoverable | a stubbed `server_error` | stopping the provider — a different branch entirely |
| The ceiling respects the budget | a test relating them | the ceiling looking small |
| Screens do not arrive together | four screens, times compared | jitter existing in the source |
| **A wall over days** | **nothing** | minutes of one screen |
| **Twenty screens** | **nothing** | four |
| **A real partition** | **nothing** | an aborted request |

---

## 6. The environment, left as found

Every probe modified the running provider, and each was reverted:

- the provider was stopped and started several times; it is running;
- the `operator` account was disabled and **re-enabled**, confirmed by reading
  it back.

**One thing worth passing on.** The first attempt at the refused end-to-end test
failed in a way that looked like the feature not working: the app redirected,
and the provider's still-live single-sign-on cookie let it straight back in, so
the screen recovered instead of refusing. That is not a defect in the product —
it is what a *browser that has recently signed in* does. Inducing a lockout
needs the account actually disabled, or the renewal actually refused; a stale
token alone is not enough.
