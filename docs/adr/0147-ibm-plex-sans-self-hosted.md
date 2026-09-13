# ADR-0147: IBM Plex Sans, self-hosted

**Status:** **Accepted**
**Date:** 2026-09-13
**Amends:** —

**Supersedes:** —
**Superseded by:** —

## Context

**No typeface is chosen anywhere in this repository.** `grep -rn
"font-family|@font-face|fontFamily" apps --include=*.css --include=*.ts
--include=*.tsx` returns nothing outside build output. Both apps inherit
Tailwind's default `--font-sans`, which is the system stack:

```
-apple-system, BlinkMacSystemFont, "Segoe UI", Roboto, "Helvetica Neue", ...
```

That is not a neutral default. It means **the fleet does not agree on what the
product looks like**: a fab's Windows kiosk resolves Segoe UI, a Linux wall
resolves whatever fontconfig picks, and a developer's Mac resolves SF. Every
spacing, alignment and line-length decision made against one of those is wrong on
the others — and the environment where the design is authored is the one that
never sees the discrepancy.

Two constraints shape the choice more than aesthetics do.

**Constitution §I: no outbound internet dependency.** A fab is air-gapped. The
ordinary way to ship a webfont — a `<link>` to `fonts.googleapis.com` — is
unavailable, and it fails in the worst way: the request hangs or fails, the
fallback paints, the layout shifts, and on an unattended wall nobody reports it.
The face must be licensed for **self-hosting and subsetting**, with no runtime
licence call of any kind.

**This product is made of numbers in columns.** Camera lists, audit logs,
timestamps, latency figures, retry counts. A face whose digits are
proportionally spaced makes every one of those tables move as values change.

## Decision

**IBM Plex Sans for UI and text, IBM Plex Mono for identifiers, paths and log
output. Self-hosted, subset, served from the app's own origin.**

- **Licence:** SIL Open Font License 1.1. Self-hosting and subsetting are
  explicitly permitted, there is no runtime activation, and there is no cost. The
  air-gap constraint is satisfied by the licence rather than worked around.
- **Figures:** Plex ships true tabular figures. Every table of timestamps and
  latencies gets `font-variant-numeric: tabular-nums` from the type tokens, not
  per call site.
- **The mono is a matched companion**, not a borrowed third family. Camera
  identifiers, fab group paths, problem-detail codes and audit payloads all
  render in it, and they sit next to Plex Sans constantly.

### What implementation must include

1. **Subset** to the glyphs actually rendered. The UI is English today; confirm
   before cutting that no fab-facing string is not.
2. **`@font-face` served from the app's origin**, `font-display: swap`.
3. **`size-adjust`, `ascent-override` and `descent-override`** tuned on the
   fallback so the swap does not move the layout. This is the step usually
   skipped and the one that matters most on a wall nobody is watching.
4. **Preload** only the faces used above the fold.
5. **An architecture test that fails the build** if any CSS under `apps/**`
   references an external font host. This repo already guards container image
   pins that way (`ContainerImagePinTests`) and the reasoning transfers exactly:
   an air-gap violation that only manifests inside a fab has to be caught in CI,
   because the failure is silent everywhere else.

## Consequences

**The fleet agrees.** Every panel, kiosk and console renders the same shapes at
the same metrics, and a spacing decision made on a developer's machine is the
decision the fab gets.

**Weights ship as static faces unless measured otherwise.** Plex has a variable
release; on an air-gapped LAN the transfer cost of either is effectively zero, so
the choice is about tooling rather than bytes. Start static, and revisit only if
the type scale turns out to want optical sizing.

**Rejected: Inter.** The safest possible choice — outstanding at UI sizes,
variable, superb tabular figures, free. It loses on ubiquity: it is the default
face of modern web software, and a product that adopts it inherits no identity.
Given that the whole point of this programme is that the product currently has
no visual identity at all, defaulting to the most-used face available would
answer the wrong question.

**Rejected: SF Pro.** The literal Apple face, and disqualified outright rather
than on taste — its licence permits use only in software for Apple platforms. It
cannot ship in a web app served to Windows and Linux kiosks. Naming it here so
the question is not reopened.

**Rejected: Geist.** Contemporary, free, strong mono companion, engineered for
dense developer tooling — genuinely close. It loses on maturity and language
coverage: it is a young family, and this product's next market is a fab that may
not be English-speaking. Plex's coverage is the safer bet for the same money,
which is none.

**Rejected: a licensed face** (Söhne, ABC Diatype, GT America and similar). This
is what a design-led company buys, and it would produce the most distinctive
result. It was rejected for now on risk rather than cost: most foundry licences
are written for public websites, and their self-hosting and domain terms do not
obviously cover an on-premise deployment inside a customer's private network
with no reachable licence server. Revisitable — but it needs a licence read by
someone willing to sign it, not an engineer's reading of a web page.
