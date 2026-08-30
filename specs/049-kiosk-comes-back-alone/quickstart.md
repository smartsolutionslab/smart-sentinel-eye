# Quickstart — 049 a wall comes back on its own

How to see the failure, and how to see it fixed. Written so someone who did not
build it can reproduce both.

---

## Seeing it fail (before)

**Two failures, and they need different provocations.** Reproducing only the
first leaves the more frequent one invisible.

### The reboot

1. Sign a kiosk in and let it show a wall.
2. **Close the browser entirely** — not the tab, the process. This is what a
   power cut does.
3. Open it again at the kiosk URL.

It asks for credentials. Nothing on the device remembered anything, because the
tokens lived in storage tied to the browser process.

### The one nobody notices

Leave a kiosk running and untouched for **more than ten hours**. It drops to the
same prompt without anything having happened to it — the sign-in session has a
hard ceiling regardless of activity.

**This is the failure that matters more**, because it happens roughly twice a
day per screen on a wall that never reboots at all. It is also the one no short
test can see: anything under the ceiling passes with the defect fully present.

---

## Seeing it fixed (after)

Same two provocations, and neither prompts for anything.

For the second, **shorten the ceiling on a test realm** rather than waiting ten
hours. That demonstrates the mechanism, not the production configuration, and
any note claiming otherwise is overstating what was done.

---

## Checking it properly

Every check must start from **no tokens at all** — clear the browser's storage
first. A check that begins signed in proves nothing about coming back, which is
the entire subject. This is the third feature running where the convenient
fixture is the one that hides the defect.

```sh
cd apps/kiosk-web && npx vitest run
```

The whole frontend job, as CI runs it, and read the **exit codes** — counting
matching output lines reported a false pass in the last feature:

```sh
pnpm format:check
pnpm -r --filter "./apps/**" lint
pnpm -r --filter "./apps/**" typecheck
pnpm -r --filter "./apps/**" test
```

---

## What none of that establishes

- **Twenty screens recovering together.** The target names twenty; a test proves
  one. Nothing in CI reboots a wall.
- **Ten real hours.** Only a shortened ceiling is demonstrable.
- **That the exposure trade was the right one.** A grant surviving a restart
  means a powered-off stolen device now yields something. That is a judgement
  recorded in the ADR, not a thing a test can settle.

The verification note must say which of these happened and which did not.
