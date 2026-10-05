# ADR-0165: An overlay element is one value object with a kind and a colour

**Status:** **Accepted**
**Date:** 2026-10-04
**Supersedes:** —
**Superseded by:** —

**Amends:** ADR-0164, in scope only. The cap of 8 now counts elements of every kind,
not labels; the number is unchanged. **Scopes:** ADR-0148's "components cite semantic
tokens only", for authored content (§3).

**Relates to:** ADR-0112 (an owned payload member has no identity of its own; the
precedent for payload-shape changes), ADR-0104 (the revision payload is the part that
legitimately varies), ADR-0073 / ADR-0040 (contract versioning, primitives on the wire),
ADR-0147 (self-hosted assets; why icons are not decided here), ADR-0123 (the
composite-and-render measurement), constitution §II, §III, §IV. Delivered by spec 300
(#2349).

**Drafting note.** Drafted during spec 300's phases 1–3. The element model, the cap's reach
and the naming were approved at the phase-1 gate as drafted. The decision then evolved in
three steps, all made by the product owner on 2026-10-04:

1. The first draft modelled colour as a closed five-entry palette. **The product owner
   decided colour is arbitrary.** The draft was revised to opaque `#RRGGBB`, with a fixed
   85% text-surface translucency and alpha explicitly excluded.
2. **The product owner confirmed that strokes carry no readability floor.**
3. **The product owner required alpha**, and accepted this ADR on that basis.

§2 records the final design. Revising for alpha also exposed a flaw in the intermediate
draft, and §2 says so: the draft's fixed 85% translucency on arbitrary text surfaces had a
true worst-case contrast of 3.5:1, not the 4.5:1 its wording implied. The palette and the
opaque-only design survive in "Alternatives considered".

## Context

Constitution §III charters Overlay Designer with "overlay primitives". Until spec 300 the
only one was a text label: `Label(Text, NormalizedPosition, NormalizedSize, FontSizePx)`.
Spec 150 (ADR-0164) made a revision hold an ordered, owned collection of 1..8 such labels,
keyed `(revision_id, ordinal)`.

#2349 asks for non-text primitives. Spec 300 delivers a box and an ellipse, and defers a
rule/divider and an icon/glyph. The 2026-09-26 product request adds an author-chosen colour
on every primitive. Three questions are architectural and no existing ADR answers them:

1. **Is a primitive kind a variant of one type, or a type of its own?**
2. **What is a colour, and how does a text label stay readable on any of them?**
3. **Does ADR-0164's cap of 8 count non-text primitives?** ADR-0164 explicitly left this
   to #2349.

Two constraints bound the answers. Elements are an **EF Core owned collection**, and owned
types do not support inheritance. The **composite-and-render leg is already over its
50 ms budget** (ADR-0123 p50 54.2 ms; spec 225 CI mean p50 56.63 ms), so anything computed
per element per paint must be trivially cheap.

## Decision

### 1. One value object, `OverlayElement`, discriminated by `ElementKind`

- `OverlayElement` replaces `Label`. It carries `ElementKind Kind` (`Text`, `Box`,
  `Ellipse`), `NormalizedPosition`, `NormalizedSize`, `OverlayColor Color`, the spec 150
  ordinal, and a **nullable `TextContent` component (text and font size), present exactly
  when `Kind == Text`**.
- Three factories (`TextElement`, `Box`, `Ellipse`) are the only public constructors and
  enforce that presence rule. A database `CHECK` backs it.
- On the wire, `OverlayElementV3` is the same shape flattened to primitives, inside a clean
  `OverlayRevisionPublishedV3` cut.

Why one type rather than a hierarchy:

- **Persistence.** EF Core does not map inheritance on owned types. A hierarchy would mean
  either giving elements identity, which reverses ADR-0112 §2, or packing each element into
  an opaque column, as `RuleActionColumnConverter` does. *Recorded as an assumption, to be
  re-checked against the pinned EF Core 10.x at implementation.* The reasons below decide
  the question even if it is lifted.
- **Shared shape.** Ordinal, geometry, colour and the whole revision lifecycle are common.
  `RuleAction` is a hierarchy because its variants share nothing; these share nearly
  everything.
- **The wire is flat anyway.** Primitives-only contracts (ADR-0040) carry a type tag and
  nullable fields whatever the domain does.
- **One renderer.** One component with one switch keeps every kind a direct child of the
  tile's aspect-ratio box (spec 150 FR-012).

**Revisit trigger:** when a third kind-specific component would be added (an icon will need
an icon name; a rule may need an orientation), revisit this decision rather than extend it
again.

**Naming.** The code term is **element** (`OverlayElement`, `ElementKind`, `MaxElements`,
`overlay_revision_elements`, `elements` on the wire). §III says "primitive", but in this
repository "primitive" means a C# built-in type (§II, `PrimitiveBoundaryTests`,
`Shared.Kernel.Primitives`), and one word must not name two things.

### 2. Colour is an arbitrary sRGB value with alpha; text must be legible over any video

**Representation.** `OverlayColor : IValueObject<string>` holds **`#RRGGBBAA`**.
- `From` accepts `^#[0-9A-Fa-f]{6}([0-9A-Fa-f]{2})?$`. Six digits means alpha `FF`.
- The stored form is always eight upper-case digits, and a private constructor makes that
  canonical form an invariant.
- Shorthand, names and functional notation are refused.
- The database repeats the pattern (upper-case, eight digits) in a `CHECK`.

**Hex, not RGBA components.** Every consumer uses the colour as CSS, and CSS Color 4 spells
it `#RRGGBBAA`. That is one column and one wire field instead of four of each. The pattern
alone bounds every channel to 0–255. `string` is legal under §II as a value object's own
backing value.

**Meaning per kind.**
- **`Box` / `Ellipse`:** the stroke, in exactly the authored colour, **alpha `00`–`FF`, no
  floor**. An invisible stroke is permitted, as authoring taken literally (gate decision:
  strokes carry no readability guarantee).
- **`Text`:** the label surface, in exactly the authored colour. **The author's alpha is the
  surface's opacity.** The fixed 85% translucency of the intermediate draft is withdrawn.

**Text legibility: the guarantee, the rule, and why the floor is per colour.** A
translucent surface over moving video has no fixed luminance. The only defensible claim is
a **worst case over every possible video pixel**:
- for white ink the worst video is pure white, which lightens the composite;
- for black ink it is pure black;
- composite luminance is monotonic in each video channel, so these two extremes bound every
  frame.

Measured over the sRGB cube (spec 300, 2026-10-04), **no single fixed opacity floor below
100% keeps 4.5:1 for every colour**:

| Fixed floor | 100% | 95% | 90% | 85% | 80% | 70% |
|---|---|---|---|---|---|---|
| Worst-case minimum, sRGB compositing | 4.58 | 4.19 | 3.82 | 3.50 | 3.19 | 2.68 |

Mid-tones lose contrast to the first few percent of bleed-through, while white and black
tolerate a great deal. The floor is therefore **derived per colour**:

> **A `Text` colour is valid if and only if, at its own alpha, the better of black and white
> ink has a contrast of at least 4.5:1 against the surface composited over the worst video
> pixel, under both sRGB-encoded and linear-light alpha compositing.**

**Compositor independence** is deliberate. Browsers composite in gamma-encoded sRGB by
default, but floors derived from that model alone fall to 2.05:1 for dark surfaces if a
compositor works in linear light. The rule takes the lighter composite over white video and
the darker over black video across both models, so the guarantee does not depend on a
kiosk's GPU path.

Formally, with `Ls` the WCAG luminance of the opaque RGB and `A` the alpha:
- `lighter = max(lum(A·rgb + (1−A)), A·Ls + (1−A))`;
- `darker = min(lum(A·rgb), A·Ls)`;
- `contrastLight = 1.05 / (lighter + 0.05)`;
- `contrastDark = (darker + 0.05) / 0.05`;
- **legible ⇔ `max(contrastLight, contrastDark) ≥ 4.5`**, and the **ink** is the larger of
  the two (ties go to dark).

At alpha `FF` this reduces exactly to the opaque threshold, L > 0.1791 → black ink, where
both inks give 4.58:1. The worst case rises monotonically with alpha (checked over the
cube), so each colour has a well-defined **minimum alpha**. **Every opaque colour qualifies**
(exhaustive minimum 4.5826:1). The resulting floors range from **`75` (45.9%) for white**,
the global lowest, through `D1` (82%) for black and `F9` (97.6%) for a signal red, to `FE`
(99.6%) for the worst mid-grey.

**What is guaranteed, precisely:** for every accepted `Text` element, the painted ink has
≥ 4.5:1 contrast against the element's surface composited over **any single video colour**,
under either compositing model. **Not claimed:**
- legibility of a glyph over **non-uniform** video (an edge or gradient behind one
  character), which is perception, not a contrast ratio;
- anything for strokes;
- any size or weight distinction, since 4.5:1 is applied at every size.

**The floor is not a product parameter.** It is derived from the 4.5:1 ratio, the reading
"readable over any video", and the two pure inks. The inks must stay pure black and pure
white, because every floor depends on the extremes.

**Where it lives.** Legibility is a **write-time invariant**: the domain's
`TextLegibility` service, called by the `Text` factory, refuses an illegible colour, and
the 400 names that colour's minimum alpha. The client's Zod schema runs the same rule to
report it before submit. **Ink selection** is the presentation half and happens in the
shared frontend style function at paint time. It costs three luminance evaluations per text
element, with no DOM read and no search. The two implementations are pinned to agree by
**one shared test-vector fixture**, generated by an independent reference script. The
database carries a **coarse** text floor (alpha ≥ `75`, a necessary condition that can
never reject a valid row) rather than a third copy of the formula.

**Default `#FFFFFFD9`.** This is the migration backfill and the console's create default.
It is white at 217/255 = 85.1%, the nearest 8-bit alpha to `--color-bg-label`'s 85%, with a
worst case of 14.9:1. For exactly this value the style function emits today's tokens
(`var(--color-bg-label)`, `var(--color-fg-on-label)`), and a test pins the token's formula
to white at 85%. The special case is therefore an equivalence within one alpha step, and
every label authored before spec 300 renders unchanged. Today's gray-900 ink keeps a
worst-case 12.7:1 on that surface.

### 3. Authored colour is data, not a token

ADR-0148 says components cite semantic tokens only. That rule governs UI chrome: values the
design system owns, themes and maps for `forced-colors`. **An authored overlay colour is
content an operator entered, and has no fixed set a token could name.** It reaches CSS as an
inline style value (`background` / `border` with the eight-digit hex), the way an element's
geometry already does. The inks the rule picks **are** design-system values, and they are
tokens: `--color-overlay-ink-dark: var(--black)` and `--color-overlay-ink-light:
var(--white)`, not redefined per theme, like `--color-bg-label`. The authored value is
validated at three layers (domain, database `CHECK`, client schema). The renderer does not
re-validate, because validation belongs at the boundary that receives input, and a text
colour's legibility cannot change after it is written.

### 4. The cap counts every element

A revision holds at most **`MaxElements = 8`** elements of any mix of kinds. ADR-0164
reasoned about nodes painted per tile per frame (9 tiles × 8 = 72). A box or ellipse node is
a bordered element with no text layout, no shadow and no animation, and costs no more than
the text node it replaces. Counting it keeps ADR-0164's worst case at 72 with no new
measurement. A separate per-kind allowance would raise that ceiling, and ADR-0164 says a
raise needs a measured ADR. Spec 300 still measures the mixed case at full cardinality.

## Consequences

- **Spec 300 can be built as specified**: one owned collection, one renderer switch, one V3
  contract cut. `ResolvedOverlayTextChangedV2` is unchanged; a non-text element contributes
  `""` to its index-aligned list.
- **Per-kind rules are runtime rules, not types.** "A Box carries no text" and "a text
  colour is legible" live in factories, a guard and (coarsely) a `CHECK`, and are tested
  red. That is the price of §1.
- **Text is readable over any video; strokes are not guaranteed visible.** Every accepted
  text colour clears 4.5:1 against its worst-case composite. A stroke of any colour or
  alpha, including fully transparent, is painted as authored, and a dark stroke over dark
  video can vanish. Both halves are gate decisions. A halo or contrasting outline would be a
  render-cost decision on an over-budget leg, and is not made here.
- **Mid-tone text cannot be made very translucent.** A signal red needs 97.6% opacity and
  the worst mid-grey 99.6%, while white needs only 45.9%. That is what 4.5:1 over arbitrary
  video costs, and it is a consequence, not a parameter. Relaxing it would mean a weaker
  ratio or a "typical video" assumption instead of the worst case. Either is a new decision
  that would reopen this ADR.
- **The domain computes luminance.** Legibility is an invariant of what may be stored, so
  `TextLegibility` lives in OverlayDesigner's domain. The same formula also lives in the
  shared frontend for ink selection and form feedback. The two copies are held together by
  one shared test-vector fixture generated by an independent reference script. A change to
  the rule means regenerating the fixture and failing both test suites until both
  implementations follow.
- **One deliberate equality special case** (`#FFFFFFD9` → today's tokens) exists in the
  style function. It is pinned by a test that ties the token's formula to white at 85%, so
  the two cannot drift silently.
- **The console's colour control needs more than the native picker.** `<input
  type="color">` has no alpha. Spec 300's PR-B pairs it with an opacity slider whose minimum
  is the hue's computed floor, so an author cannot drag into an illegible value.
- **Icon/glyph remains undecided** and needs its own ADR of ADR-0147's kind: icon set,
  licence, subsetting, self-hosting and an external-host build check. §1's revisit trigger
  is likely to fire when it lands.

## Alternatives Considered

- **A type per kind (sealed hierarchy, `RuleAction`-style).** Rejected for the reasons in
  §1. It is the right shape when variants share nothing, and these share almost everything.
- **Elements as entities with identity and TPH inheritance.** Rejected: it reverses
  ADR-0112 §2 to get a mapping feature, and gives a payload member an identity nothing
  addresses.
- **A closed named palette** (`Neutral`, `Red`, `Amber`, `Green`, `Blue`, each a vetted
  surface/ink/stroke token triple). This was the first draft, and is architecturally
  simpler: no computed ink, tokens throughout. **Rejected by the product owner**, who
  required arbitrary colour.
- **Opaque `#RRGGBB` with a fixed 85% text translucency** (the intermediate draft).
  **Superseded when the product owner required alpha.** It was also weaker than it read:
  its worst-case contrast for mid-tone surfaces was 3.5:1.
- **One fixed text opacity floor for every colour** (for example 70% or 85%). Rejected: no
  fixed floor below 100% keeps 4.5:1 (§2's table). A floor of 100% would forbid translucent
  text, including today's default label. The per-colour floor keeps the guarantee and
  allows more transparency wherever the colour can afford it.
- **A worst-case rule under sRGB compositing only.** Rejected: its floors fall to 2.05:1 if
  a compositor works in linear light. The guarantee should not depend on a GPU path.
- **The exact legibility rule in a database `CHECK`.** Rejected: a third implementation of
  the formula, outside the shared vectors, invites drift. The coarse `75` floor is the
  backstop.
- **RGBA components (`OverlayColor(byte R, byte G, byte B, byte A)`).** Rejected: four
  columns, four wire fields and re-serialisation to CSS at every paint site, for a value
  nothing does arithmetic on except the legibility rule, which reads hex just as cheaply.
- **Computing ink by contrast search at render**, trying many candidate inks and keeping the
  best. Rejected: comparing the two pure inks is already optimal for a black-or-white
  choice, and anything more costs time on an over-budget leg.
- **Computing ink on the server and sending it on the wire.** Rejected: it puts a
  presentation choice in the contract. The client needs the formula anyway, for the editor
  preview and form feedback. Validity is enforced server-side; the choice of ink is not
  sent.
- **Separate caps per kind.** Rejected: it raises the per-wall node ceiling above 72 without
  the measurement ADR-0164 requires for a raise.
