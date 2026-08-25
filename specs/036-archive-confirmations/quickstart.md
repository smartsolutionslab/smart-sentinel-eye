# Quickstart: Archiving asks before it happens

**Feature**: `036-archive-confirmations` · 2026-08-25

How to see this working, and how to prove the three things most likely to be
wrong. All three are about what the confirmation **says**, not whether one
appears.

```sh
dotnet run --project src/AppHost
```

---

## 1. Nothing archives on one click

On each of Rules, Overlays, Layouts and System variables:

```
Click Archive → read → Cancel
```

> **Expect**: nothing archived, four times. The row is unchanged and still
> offers Archive.

Then confirm one, and check it archived **once** — not twice, and not zero
times.

---

## 2. Read all four. They must not say the same thing

**This is the check most likely to be skipped, because the feature works
without it.**

| Page | Must say |
|---|---|
| Rules | cannot be published again; a replacement means **cloning** it |
| Variables | the current value is **cleared**; it can never be given another |
| Layouts | **can never be edited or published again** |
| Overlays | **can never be edited or published again** |

> Each must **name** its subject — the rule, the variable, the layout or overlay
> and which revision. If any says *"Are you sure?"*, that is the phrase this
> feature exists to replace.

> If the layout and overlay ones say only *"this cannot be undone"*, the sharpest
> sentence in the feature has been softened away. True of all four; it
> understates these two, which become **unusable**, not merely archived.

---

## 3. The kiosk warning is conditional

```
a) Archive a PUBLISHED layout revision  → must warn that kiosks are sent away now
b) Branch a draft, then archive the DRAFT → must NOT mention kiosks at all
```

> Archiving a draft strands nothing — a published revision still exists to
> branch from — and no kiosk is showing a draft.

A confirmation that warns either way overstates the safe case, and an overstated
warning is one operators learn to click through. That costs more than the
warning buys.

---

## 4. It really disconnects

With a kiosk showing a layout, archive that layout's published revision.

> **Expect**: the kiosk leaves that layout immediately.

Worth doing once by hand. It is the consequence an operator cannot see from the
layouts page, and the reason the sentence is in the confirmation rather than in
a release note.

---

## Automated equivalents

| Check | Where |
|---|---|
| 1 | Each page's test — dismiss asserts **zero** calls; confirm asserts **one** |
| 2 | Each page's test — the required sentence per page, on rendered text |
| 3 | `LayoutsPage.test.tsx`, `OverlaysPage.test.tsx` — published warns, draft does not |
| 4 | Covered by the kiosk's existing `onArchived` tests; not re-proved here |

**Note**: `RulesPage.test.tsx` has an existing test that clicks Archive and
asserts the request. It is **updated**, not deleted — it gains the confirmation
step and keeps the assertion, which then proves both that the confirmation is
required and that confirming sends exactly what it sent before.
