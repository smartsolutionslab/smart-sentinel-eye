# Data model — 049 a wall comes back on its own

No persisted entity changes, no domain code, no context boundary crossed. What
follows is what a kiosk holds, where, and for how long — which is the whole
subject.

---

## What a kiosk holds

| | Today | After |
|---|---|---|
| **Where it is kept** | Storage tied to the browser process | Storage that survives the process |
| **Survives a restart** | **No** — unconditionally lost | Yes |
| **Survives ten hours** | **No** — the session ends on a clock | Yes |
| **Authority** | View-only, one fab | **Unchanged** |
| **Whose it is** | Whoever signed in | Whoever authorised the screen |
| **Revocable alone** | Yes | Yes |

**Only two rows change**, and they are the two the target needs. Authority is
deliberately identical: unattended recovery must not be bought with a broader
grant, and a reviewer should be able to check that quickly.

---

## The exposure this changes

Stated as a table because it is the cost of the feature and should not need
reading prose to find.

| Situation | Today | After |
|---|---|---|
| Device powered off, stolen | Yields nothing | **Yields a usable grant** |
| Device running, stolen | Yields a grant | Yields a grant |
| Grant withdrawn centrally | Screen stops | Screen stops |
| Blast radius of one device | That screen | That screen |
| What the grant permits | View one fab | View one fab |

**Row one is the whole trade.** Everything else is unchanged. A powered-off
kiosk becomes worth stealing in a way it was not before, and the mitigations are
that the grant is that device's alone, independently withdrawable, and view-only
in a single fab — not the storage it sits in, which is readable on the machine
it belongs to.

---

## States a screen can be in

Four, and three of them currently look identical to whoever is standing there:

| State | What it means | Resolves by itself? |
|---|---|---|
| Showing its wall | Working | — |
| Never enrolled | A setup step has not happened | No — needs a person, once |
| No longer trusted | Access was withdrawn deliberately | No — and it should not |
| Cannot reach the identity service | Something is down | **Yes** — must keep retrying |

The last must retry without a person, because it clears on its own. The middle
two must not pretend to: a screen that retries a withdrawn credential forever is
telling whoever watches it that the problem is transient when it is not.

---

## Invariants

1. **Authority does not grow.** Whatever a kiosk may see after this change, it
   could see before.
2. **One screen's grant is one screen's.** Withdrawing it stops that screen and
   no other. This is what makes the exposure survivable and is worth a test of
   its own.
3. **A withdrawn screen stops without waiting for a restart.** Otherwise
   withdrawal means "at some unspecified future time", which is not withdrawal.
4. **No credential ships in the build.** What a device holds, it acquired.
