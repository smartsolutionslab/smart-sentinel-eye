# Spec 300: The overlay that could only be text

**Issue:** #2349 · **Branch:** `feat/2349-the-overlay-that-could-only-be-text` (not yet cut)
**ADRs:** **ADR-0165** (`docs/adr/0165-an-overlay-element-is-one-value-object-with-a-kind-and-a-colour.md`,
**Accepted** 2026-10-04) for the element model, arbitrary colour with alpha, the text
legibility rule and computed ink, and the cap's reach. Also ADR-0164 (the cap this spec extends from labels to
elements), ADR-0112 (the governing precedent for payload-shape changes), ADR-0148 (semantic
colour tokens), ADR-0147 (self-hosted assets, the reason icons are out of scope), ADR-0073 /
ADR-0040 (contract versioning, primitives on the wire), ADR-0067, ADR-0113, ADR-0142,
ADR-0129, ADR-0123. Constitution §II, §III, §IV, §VII.

**Written 2026-10-04 against `develop` at `b0e0d94a`**, the day spec 150 (#2345, PR #2711)
merged. Spec 150 is the shape this spec extends. Every claim about current code below was
read from that tree.

**Phase 4a colour:** **two colours, split per artefact** (plan.md §"Phase 4a").
CHARACTERISATION (captured green before any change, passing unmodified after) covers the
wall's existing text-label render, the shared editor, and the rename. RED covers every
non-text kind and every non-default colour. Where the split is unclear, the artefact is red.

---

## The premise, checked

The issue says *"a highlight box already exists as a runtime concept: the wall draws one on
a tile"*. That is true, but it supports less than it seems to. `.ssE-overlay-highlight`
(`apps/kiosk-web/src/styles/index.css:13`) is a **ring around the whole tile**: an `outline`
plus a pulsing `box-shadow` on the tile element. It has no geometry of its own. What it
proves is that the wall can paint an accent-coloured outline over video, and that is the
rendering precedent this spec reuses for the box stroke. **It does not prove the wall can
place a box at authored coordinates. That part is new.** An authored box sits at
`normalizedX/Y/Width/Height` inside the tile's aspect-ratio box, which is exactly where a
text label sits today (`CameraViewer.tsx:533`).

The issue's other premise holds exactly. The geometry already generalises: `Label` is
`Text + NormalizedPosition + NormalizedSize + FontSizePx` (`Label.cs:18`), and three of
those four describe a box. Spec 150 also left two things this spec reuses: an ordered,
owned collection keyed `(revision_id, ordinal)`, and the rule that a missing text
contributes `""` to the index-aligned resolved-text list instead of shifting it (`e87a4e9d`).

---

## The decision this spec makes: one element type with a `kind`

The issue asks whether non-text primitives are **variants of one type** (a shape with a
`kind` discriminator, sharing geometry and lifecycle) or **separate types**.

**One value object, `OverlayElement`, with an `ElementKind` discriminator and a shared
`OverlayColor`, is chosen.** Kind-specific state lives in a nullable component (text and
font size are present exactly when `Kind = Text`). Five reasons, in order of weight:

1. **The persistence model cannot express the alternative cleanly.** Elements are an EF
   Core **owned** collection (`OverlayConfiguration.cs:132`, spec 150). EF Core does not
   support inheritance on owned entity types. A `TextElement : OverlayElement` /
   `BoxElement : OverlayElement` hierarchy would mean either promoting elements to entities
   with identity, which reverses ADR-0112 §2's "a member has no identity of its own", or
   packing each element into an opaque string column the way `RuleActionColumnConverter`
   packs `RuleAction`. That is a regression from spec 150's queryable columns, made the day
   after it shipped. *(Assumption to confirm at T005 against the pinned EF Core 10.x: owned
   types still reject inheritance. If 10.x now supports it, reason 1 weakens and reasons
   2–5 still decide.)*
2. **The variants share almost everything.** Ordinal, position, size, colour, revision
   membership and the whole Draft→Published→Archived lifecycle are common. Only `Text`
   carries extra state. The repo's one discriminated-subtype VO, `RuleAction`, fits there
   because its variants share **nothing**: `SetVariableValue` and `HighlightOverlay` have no
   field in common. These elements are the opposite case.
3. **The wire needs a discriminator anyway.** `Shared.Contracts` carries primitives only
   (ADR-0040). A polymorphic record on the wire is a flat record plus a type tag, so the
   flat shape exists at the boundary whichever model the domain uses. One flat domain shape
   means no mapping layer that exists only to undo a hierarchy.
4. **One renderer, one switch.** The wall's composite (`CameraViewer`) paints each element
   as one absolutely positioned node inside the same containing block. A `kind` switch
   inside one component keeps FR-012 of spec 150 (no wrapper element, a direct child of the
   aspect-ratio box) true for every kind by construction.
5. **The cost is bounded and visible.** The one drawback is that kind-specific invariants
   move from the type system into factories and a guard, for example "a Box carries no
   text". That is a closed set of rules, tested red, backed by a database `CHECK`
   constraint, with no switch default (plan §"Invariants").

**When to revisit:** if a later kind needs two or more fields that no other kind uses (the
icon kind will need an icon name; a rule kind may need an orientation), each lands as one
more nullable component. When a third kind-specific component appears, ADR-0165 should be
revisited rather than extended again. That is the "primitives diverge a lot later" case the
issue author named.

### Colour is a shared field: arbitrary, with alpha

Following the issue's 2026-09-26 comment, colour is **a field on every element, not a
kind**. Three gate decisions shape it, all final (2026-10-04):
1. colour is **arbitrary**, not the five-entry palette first drafted;
2. strokes carry **no readability guarantee**;
3. colour **carries alpha**.

What follows is how that is represented, and exactly what a text element's readability
guarantee is once its surface can be transparent.

**Representation: `OverlayColor`, a value object over a hex string, canonically
`#RRGGBBAA`.** It is a `sealed record OverlayColor : IValueObject<string>` with a private
constructor and `From(string)`.

- **Accepted input:** `^#[0-9A-Fa-f]{6}([0-9A-Fa-f]{2})?$`.
- **Canonical form:** always eight digits, upper-case. `#d32f2f` becomes `#D32F2FFF`, so
  six digits means opaque and every stored value has exactly one spelling.
- **Hex, not an RGBA-components VO:**
  - It is CSS's own spelling. CSS Color 4 accepts `#RRGGBBAA` directly, so the value goes
    into a style object as-is, with no re-serialisation at each paint site on the ≤ 50 ms
    leg.
  - It needs one column (`color varchar(9)`) and one wire field (`string Color` on
    `OverlayElementV3`). Components would need four of each, for a value only the
    legibility rule below does arithmetic on, and that rule parses eight hex digits as
    cheaply.
  - Channel range is implied: two hex digits cannot leave 0–255, so the pattern is the
    whole syntactic validation.
  - §II holds: `string` is a value object's own backing value (exemption 3), exactly as
    `OverlayRevisionState.Value` is.
- **Not accepted:** shorthand `#RGB` / `#RGBA`, named colours, and
  `rgb()`/`hsl()`/`oklch()` notation.

**What colour means per kind**
- **`Box` / `Ellipse`:** the stroke, painted in exactly the authored colour, **alpha 00–FF
  with no floor**. An `…00` stroke is invisible, and that is accepted as authoring taken
  literally (gate decision 2).
- **`Text`:** the label's surface, painted in exactly the authored colour, alpha included.
  The fixed 85% translucency the earlier revision applied to every surface is **gone**: the
  author's alpha is the opacity. **A text surface's alpha has a floor that depends on its
  colour** (below).

#### Why the text floor is per-colour, not one number

The contrast an ink gets against a translucent surface depends on what shows through it,
and on a wall that is arbitrary, moving video. The defensible claim is therefore a
**worst case over every possible video pixel**. For white ink, the worst video behind the
surface is pure white, because it pushes the composite lighter. For black ink, the worst
is pure black. Composite luminance is monotonic in each video channel, so these two
extremes bound every frame.

Measured while drafting this spec (Node scripts over the sRGB cube, 2026-10-04), with the
better of the two inks chosen per surface, the minimum worst-case contrast for a single
**fixed** floor applied to every colour is:

| Fixed floor | 100% | 95% | 90% | 85% | 80% | 70% |
|---|---|---|---|---|---|---|
| Minimum contrast over all surfaces and all video, sRGB compositing | **4.58** | 4.19 | 3.82 | 3.50 | 3.19 | 2.68 |

That row uses the *more favourable* of the two compositing models (below), so a fixed
floor fares worse still under the rule this spec adopts.

**No fixed floor below 100% keeps 4.5:1.** Mid-tone surfaces (around L ≈ 0.18, where black
and white ink tie) lose contrast to the first few percent of bleed-through. That also
corrects this spec's previous revision. Its fixed 85% translucency on *arbitrary* surfaces
had a true worst case of **3.5:1** for mid-tones. "Measured against the opaque surface",
as it said, was accurate wording that hid a real gap. This revision closes it.

A fixed floor would therefore either be 100%, which forbids translucent text entirely,
including today's default label at 85%, or give up the 4.5:1 that FR-015 already promises.
Neither is needed, because **light and dark surfaces tolerate far more transparency than
mid-tones do**. White needs only 45.9% opacity to keep black ink at 4.5:1 over pure-black
video. So:

**The rule: a `Text` element's colour is valid if and only if, at its own alpha, the
better of black and white ink clears 4.5:1 against the surface composited over the worst
possible video pixel.** Equivalently, its alpha is at least that colour's **minimum
alpha**. The worst case only increases with alpha (checked monotonic over the cube), so
the minimum is well defined. **Every opaque colour qualifies**: the exhaustive minimum at
alpha FF is 4.5826:1, unchanged from the opaque design. Some resulting floors:

| Surface | Minimum alpha | | Surface | Minimum alpha |
|---|---|---|---|---|
| `#FFFFFF` white | `75` (45.9%), the global lowest | | `#FFA000` amber | `A4` (64.3%) |
| `#FFEB3B` yellow | `80` (50.2%) | | `#1565C0` blue | `F1` (94.5%) |
| `#000000` black | `D1` (82.0%) | | `#D32F2F` red | `F9` (97.6%) |
| `#767676` mid-grey | `FB` (98.4%) | | `#757575` mid-grey | `FE` (99.6%) |

**The floor is derived, not chosen.** It follows from three things already decided: the
4.5:1 ratio FR-015 commits to, the worst-case-video reading of "readable over arbitrary
video", and the two inks. **No percentage in it is a product call.** The 45.9% global
lowest is a consequence (white), not a parameter.

**Compositor-independent, deliberately.** Browsers composite a translucent background in
gamma-encoded sRGB by default (CSS Compositing). A GPU path or a future engine that
composites in linear light produces a different composite. Floors derived under sRGB alone
fall to **2.05:1** under linear compositing for dark surfaces. **The rule therefore takes
the worse of both models**: the lighter composite over white video, and the darker over
black video, whichever compositing produced it. The floors above already include this. The
guarantee does not depend on which compositor a 24/7 kiosk's GPU happens to use.

#### What is guaranteed, precisely

For every `Text` element the system accepts, **the computed ink has a contrast ratio of at
least 4.5:1 against the element's surface as composited over any single video colour,
under either sRGB-encoded or linear-light alpha compositing.**

What it does **not** claim:
- Anything about **non-uniform** video under the glyphs, such as an edge or a gradient
  passing behind a stroke of text. The bound is per pixel. Each pixel of surface is
  covered, but legibility of a glyph spanning two very different pixels is perception, not
  contrast ratio.
- Anything about **strokes**, by gate decision 2.
- Anything about **font weight or size**. 4.5:1 is WCAG's normal-text ratio, applied at
  every size.

The ink is chosen by the same comparison the validity rule makes, so the ink the wall
paints is the ink the guarantee was computed for:
- compute `contrastLight` = white ink against the worst light composite;
- compute `contrastDark` = black ink against the worst dark composite;
- the ink is the larger of the two (ties go to dark), and validity is `max ≥ 4.5`.

At alpha FF this reduces exactly to the opaque threshold rule (L > 0.1791 → black). The
cost is three luminance evaluations (nine `pow` calls) per text element, fixed, with no DOM
read and no search over candidates.

**Where the rule lives.** It is a **write-time invariant** in the domain: the `Text`
factory refuses an illegible colour, so the system cannot hold one, and the 400 tells the
author that colour's minimum alpha. It is also a **paint-time choice** in the shared
frontend style function, which picks the ink. Two implementations of one formula are
pinned to agree by **one shared test-vector fixture**: colours, expected validity and
expected ink. Both the C# and the TypeScript tests read it, and it is generated by an
independent reference script, not by either implementation.

**The inks stay pure black and pure white.** Every number above depends on the extremes.

**The default renders byte-identically to today.** The default is **`#FFFFFFD9`**: white at
217/255 = 85.1%, the nearest 8-bit alpha to `--color-bg-label`'s 85%. It is the backfill
for every existing label and the console's create default. Its worst case is 14.9:1, far
above the floor, and white's minimum alpha is `75`. For exactly `#FFFFFFD9` the text style
function **emits today's token strings** (`var(--color-bg-label)`,
`var(--color-fg-on-label)`). The existing guards assert those strings
(`OverlayLabelCharacterisation.test.tsx:78,88`), so this is what lets them pass unedited. A
test pins the token's formula to white at 85%, so the special case is an equivalence within
one 8-bit alpha step. Today's softer gray-900 ink is kept for the default because its
worst-case contrast on that surface is above 12:1. Every other colour takes the computed
ink.

**How the colour reaches CSS.** As inline style values on the element's node, the way its
geometry already does:
- Box/Ellipse: `border: … solid #RRGGBBAA`.
- Text: `background: #RRGGBBAA` and `color: var(--color-overlay-ink-dark | -light)`.

There is no `color-mix`, because the alpha is in the value. The two inks are new
**semantic** tokens over `--black` / `--white`. The authored value is data, validated at
three layers, and is the one colour that does not come from a token. ADR-0165 §3 scopes
ADR-0148 accordingly.

**The three validation layers, per kind:**

| Layer | All kinds | `Text` additionally |
|---|---|---|
| Domain | `OverlayColor.From` pattern + canonicalise | exact legibility rule in `TextElement` |
| Database `CHECK` | `color ~ '^#[0-9A-F]{8}$'` | coarse floor: alpha ≥ `75` (the global lowest) |
| Client Zod | same pattern | exact legibility rule (shared function), reporting the minimum alpha |

The database holds the **coarse** floor, not the exact rule, on purpose. The exact rule is
nine `pow` calls and two comparisons, and writing it a third time in SQL, with no share in
the test vectors, is how implementations drift. The `CHECK` is a backstop against a write
that bypassed the aggregate, and a necessary condition (no legible text colour has alpha
below `75`), so it can never reject a valid row.

### The cap counts every element

ADR-0164 left this to #2349: *"whether non-text primitives count toward ADR-0164's cap"*.
**They count. A revision holds at most 8 elements of any mix of kinds.** `MaxLabels`
becomes `MaxElements` and keeps the value 8. ADR-0164's reasoning was about **nodes drawn
per tile per frame** (9 tiles × 8 = 72), and a box or ellipse node (a bordered element, no
text layout) costs no more to paint than a text node. Counting them keeps ADR-0164's
arithmetic valid without new measurement. A separate per-kind allowance would raise the
worst case above 72, which ADR-0164 says needs a measured ADR. FR-017 still measures the
mixed case.

### Naming: `Label` becomes `OverlayElement`

A box is not a label, and §II asks for an explicit ubiquitous language. Constitution §III's
word is *"overlay primitives"*, but **"primitive" is taken in this repository**: §II,
`PrimitiveBoundaryTests` and the `Shared.Kernel.Primitives` namespace all use it to mean C#
built-in types. So the code term is **element**: `OverlayElement`, `ElementKind`,
`ElementOrdinal`, `ElementSetViolation`, `MaxElements`, table `overlay_revision_elements`,
contract `OverlayElementV3`, HTTP field `elements`. The rename rides the contract cut that
is needed anyway (below). That is the cheapest moment it will ever have.

What keeps its name: the frontend `CameraViewer` prop `overlays` and `CameraViewerOverlay`
("something drawn over the video" covers every kind). The frontend type `OverlayLabel` is
also kept, as the **text variant** of the new `OverlayElement` union, so the shared
`OverlayEditor.tsx` (which edits exactly one text label) needs no edit (FR-016).

---

## User stories

### US1: An operator outlines a region of a camera with a box (P1)

*As a control-room operator, I want to outline a machine, a doorway or a restricted zone on
a camera cell, so that the wall shows where to look and not only what to read.*

Independently shippable and observable: create an overlay whose elements are a `Box` and a
`Text` caption through the API, publish it, and see a stroked rectangle and the caption on
the wall.

### US2: Every element carries an author-chosen colour (P1)

*As an operator, I want to give each element any colour I choose, so that a restricted zone
reads red and a "running" caption reads green, while every caption stays readable and labels
I made before today look the same as they always did.*

P1 together with US1, because a box needs a stroke colour, and colour sits on the shared
base, so text gets it in the same change. Existing labels become `#FFFFFFD9` (the default)
and render as before; that is the characterisation half.

### US3: An operator marks a round feature with an ellipse (P2)

*As an operator, I want an ellipse as well as a box, for round tanks, valves and gauges.*

The same geometry and the same stroke as a box, rendered with `border-radius: 50%`. It is
separate from US1 only so it can be cut without touching US1. Its marginal cost is one
`ElementKind` value and one render branch.

### US4: The console lets an operator colour the label it edits (P3, separate PR)

*As an operator using the management console, I want to choose the colour of the label the
overlay dialog edits, without dropping to the API.*

A colour input (native `<input type="color">` plus a hex text field) in `OverlayEditorDialog`
for the text element it shows, with the preview showing the computed ink. It is cuttable and
ships as PR-B. US1–US3 are authored through the API, as spec 150's labels were. The
console's own safety (FR-019) is **not** part of US4. It ships with PR-A, because PR-A is
what makes non-text elements reachable.

### Out of scope, with reasons, and each filed before PR-A merges

- **Icon / glyph.** It needs its own decision, of ADR-0147's kind: which icon set, under
  what licence, subset how, served from the app's own origin, plus an architecture test that
  fails the build on an external icon host (ADR-0147 §"What implementation must include"
  item 5 transfers exactly). That decision is not architectural reasoning from existing
  ADRs. It is a sourcing and licensing choice. The element model leaves room for it
  (`ElementKind.Icon` plus a nullable icon-name component; see "When to revisit").
  **Separate issue, separate ADR.**
- **Rule / divider.** Box and ellipse fill their geometry. A rule does not: it is a line,
  and the spec would have to decide whether it runs along the box's longer axis, whether it
  takes an explicit orientation, and whether diagonals exist. That is a geometry decision
  box and ellipse do not need, and no runtime or authoring precedent informs it. **Separate
  issue**, small once this spec's model exists.
- **Authoring shapes in the console, and multiple elements per overlay in the console.** The
  shared editor is still single-label (#2343, open). A console that edits one element could
  make a box-only overlay, but a box with its caption needs two elements, and a one-element
  shape editor would be rebuilt by #2343's multi-element editor. This is a comment on #2343,
  not new UI here (constitution §IX, ADR-0036).
- **Box fill, stroke width and dashed strokes.** Outline only, at one fixed stroke derived
  from the tile width. Fill is additive later (one nullable component). *(Assumption,
  recorded: the issue's "outline a machine, doorway, restricted zone" reads as stroke.)*
- **Pointing the highlight action at an element.** `RuleAction.HighlightOverlay` still rings
  the tile.
- **Z-order controls** (#2348). Paint order stays the dense ordinal from spec 150.

---

## Acceptance scenarios

### Happy path

```gherkin
Scenario: A box and its caption are created, published once, and drawn on the wall
  Given an operator holding scope sse.overlays.write
  When they POST /overlays with elements
    | kind | color   | x    | y    | width | height | text      | fontSizePx |
    | Box  | #d32f2f | 0.10 | 0.20 | 0.40  | 0.50   |           |            |
    | Text | #d32f2f | 0.10 | 0.72 | 0.40  | 0.08   | Zone A    | 24         |
  Then the response is 201 Created
  And GET /overlays/{id} returns one revision whose elements array has those two entries in order
  And both colours read back as "#D32F2FFF"
  When they publish revision 1 with a matching If-Match
  Then exactly one OverlayRevisionPublishedV3 is emitted, carrying both elements
  And the wall tile bound to the overlay paints a #D32F2FFF stroked rectangle at the box's geometry
  And paints the caption on an opaque #D32F2FFF surface with white ink
```

```gherkin
Scenario Outline: A translucent colour is accepted or refused by kind
  When an operator POSTs /overlays with one element of kind <kind> and color <color>
  Then the response is <status>

  Examples:
    | kind    | color     | status | why                                         |
    | Box     | #D32F2F00 | 201    | strokes take any alpha, even invisible      |
    | Ellipse | #FFA00040 | 201    | strokes take any alpha                      |
    | Text    | #FFFFFF75 | 201    | white's minimum alpha (worst case 4.56:1)   |
    | Text    | #FFFFFF74 | 400    | one step below it (worst case 4.49:1)       |
    | Text    | #D32F2FF9 | 201    | red's minimum alpha (worst case 4.55:1)     |
    | Text    | #D32F2FF8 | 400    | one step below it (worst case 4.49:1)       |
    | Text    | #D32F2F80 | 400    | far below it (worst case 1.75:1)            |
```

```gherkin
Scenario: A too-transparent caption is refused with its colour's floor
  When an operator POSTs /overlays with a Text element coloured #D32F2F80
  Then the response is 400 Bad Request
  And the problem detail names color
  And it states that this colour needs alpha of at least F9 to stay readable over any video
```

```gherkin
Scenario Outline: A caption's ink is derived from its surface
  Given a published overlay whose only element is a Text coloured <surface>
  When the kiosk renders its tile
  Then the caption's ink is <ink>
  And the ink's contrast against <surface> composited over the worst video pixel is at least 4.5:1

  Examples:
    | surface   | ink   |
    | #FFEB3BFF | black |
    | #1565C0FF | white |
    | #757575FF | white |
    | #767676FF | black |
    | #FFFFFF75 | black |
    | #D32F2FF9 | white |
```

```gherkin
Scenario: An ellipse is drawn at its geometry
  Given a published overlay whose only element is an Ellipse coloured #FFA000CC
  When the kiosk renders its tile
  Then one overlay element node is painted with a 50% border radius and a #FFA000CC stroke
  And it is hidden from assistive technology
```

```gherkin
Scenario: A label published before this feature is unchanged
  Given an overlay revision persisted before the migration ran
  When it is read back
  Then its single element has kind Text and color "#FFFFFFD9"
  And the kiosk paints it with computed style and geometry identical to before the migration
  And its background and ink are still var(--color-bg-label) and var(--color-fg-on-label)
```

```gherkin
Scenario: A placeholder still resolves when a shape precedes the label
  Given a published overlay whose elements are a Box at ordinal 0 and a Text "{{line-1-temp}}" at ordinal 1
  When the value of line-1-temp changes
  Then one ResolvedOverlayTextChangedV2 is emitted for the overlay
  And its ResolvedTexts are ["", "<new value>"]
  And the caption at ordinal 1 shows the new value on the wall
```

```gherkin
Scenario: Editing a mixed overlay in the console keeps its shapes
  Given a draft revision whose elements are a Box, then a Text, created through the API
  When an operator opens it in the console's overlay dialog
  Then the dialog edits the Text element
  When they change its text and save
  Then the PATCH body carries both elements, the Box byte-identical and first
```

### Conflict

```gherkin
Scenario: A stale If-Match loses the race on an element edit
  Given two operators holding the same overlay at version 4
  When the first replaces the draft's elements and succeeds
  And the second replaces them with If-Match: 4
  Then the second receives 409 Conflict
  And the element set is the first operator's, entire
```

```gherkin
Scenario: A published revision's elements cannot be edited
  Given a revision in the Published state
  When an operator PATCHes its elements
  Then the response is 409 Conflict
```

### Bad request

```gherkin
Scenario Outline: A malformed element rejects the whole set
  When an operator POSTs /overlays with one valid Text element and <bad element>
  Then the response is 400 Bad Request
  And no overlay is created
  And the problem detail names <field>

  Examples:
    | bad element                         | field      |
    | kind "Arrow"                        | kind       |
    | color "red" (a name, not hex)       | color      |
    | color "#FF00"     (4 digits)        | color      |
    | color "#FF0000A"  (7 digits)        | color      |
    | color "#FF0000AAB" (9 digits)       | color      |
    | color "#GG0000"   (non-hex digit)   | color      |
    | no color                            | color      |
    | a Box carrying text "x"             | text       |
    | a Text with no text                 | text       |
    | a Box carrying fontSizePx 24        | fontSizePx |
    | a Box of zero width                 | normalizedWidth |
```

```gherkin
Scenario: Shapes count toward the ceiling
  When an operator POSTs /overlays with 5 Box elements and 4 Text elements
  Then the response is 400 Bad Request
  And the problem detail names the ceiling of 8 elements
```

### Auth

```gherkin
Scenario: A caller without the write scope cannot create a shaped overlay
  Given a caller authenticated without sse.overlays.write
  When they POST /overlays with a Box element
  Then the response is 403 Forbidden
```

```gherkin
Scenario: A caller without the read scope cannot read one
  Given a caller authenticated without sse.overlays.read
  When they GET /overlays/{id}
  Then the response is 403 Forbidden
```

### Idempotency (ADR-0142, already wired on this route)

```gherkin
Scenario: A retried create with the same key returns the first answer
  Given an operator POSTs /overlays with a Box and a Text and Idempotency-Key: k1
  When they replay the identical request with Idempotency-Key: k1
  Then the response is the original 201 and identifier, and exactly one overlay exists
```

---

## Functional requirements

- **FR-001** A revision carries an ordered set of 1..`MaxElements` elements. `MaxElements =
  8`, the value ADR-0164 set, now counting every kind (ADR-0165).
- **FR-002** `ElementKind` is a closed set: `Text`, `Box`, `Ellipse`. An unknown kind is a
  400 at the API edge. Domain switches over kind have no default arm that quietly succeeds.
- **FR-003** `OverlayColor` is an arbitrary sRGB colour with alpha.
  - Requests must match `^#[0-9A-Fa-f]{6}([0-9A-Fa-f]{2})?$`. Six digits means alpha `FF`.
  - The value is stored and returned as **eight upper-case digits**.
  - Anything else is a 400 naming `color`.
  - Alpha range **per kind**: `Box` / `Ellipse` take `00`–`FF`. A `Text` colour must be
    **legible**: at its alpha, the better of black and white ink clears 4.5:1 against the
    surface composited over the worst video pixel, under both sRGB-encoded and linear-light
    compositing (spec §"Why the text floor is per-colour"). An illegible text colour is a
    400 naming `color` and stating that colour's minimum alpha.
  - `color` is **required** on every element in requests; there is no silent default on the
    wire. `OverlayColor.Default = "#FFFFFFD9"` is used only by the migration backfill and
    the console's create form.
  - The database `CHECK` enforces the pattern for every kind, and alpha ≥ `75` (the global
    lowest legible text alpha) for `Text`.
- **FR-004** A `Text` element carries text (1..256, trimmed, as today) and a font size
  (8..256, as today). A `Box` or `Ellipse` carries **neither**. Either violation is a 400
  naming the field, and the aggregate also enforces it as a backstop.
- **FR-005** Geometry rules are unchanged and shared by every kind: position in [0,1], size
  in (0,1].
- **FR-006** Overlap, duplication and order rules from spec 150 (FR-004/005/006/007) carry
  over unchanged to elements: overlap allowed, dense ordinal as paint order, wholesale
  replace on edit, deep copy on branch.
- **FR-007** Existing rows are backfilled to `kind = 'Text'`, `color = '#FFFFFFD9'` in one
  migration. The table and columns are renamed in the same migration (plan §"Migration").
  Zero data loss and no read window.
- **FR-008** `OverlayRevisionPublishedV2` is replaced by **`OverlayRevisionPublishedV3`**
  carrying `IReadOnlyList<OverlayElementV3>`, with V2 deleted in the same feature. This is a
  clean cut, ADR-0073's "semantic shift": a list called `Labels` that holds boxes, with
  `Text` turning nullable, is a changed meaning, not an additive field.
- **FR-009** `ResolvedOverlayTextChangedV2` is **unchanged**. Its `ResolvedTexts` stays
  index-aligned with the element list, and a non-text element contributes `""`. This is the
  rule spec 150 already enforces in the seeder (`e87a4e9d`), now stated for every producer.
  No field changes and no consumer reads it differently, so ADR-0073 does not bump it.
- **FR-010** `OverlayRevisionArchivedV1` is unchanged.
- **FR-011** SystemVariables registers placeholders from `Text` elements only. Its key
  (`overlayIdentifier`) and its one-version-bump-per-overlay rule are unchanged.
- **FR-012** The wall paints every element as a direct child of the tile's aspect-ratio box,
  with no wrapper, in ordinal order. Box and Ellipse are stroked and unfilled. Text keeps
  `overlayLabelSurfaceStyle`.
- **FR-013** The stroke uses **no animation, no `box-shadow` and no filter**. The highlight's
  pulse is deliberately not reused: 72 animated nodes would repaint every frame on a leg
  already over budget.
- **FR-014** Box and Ellipse nodes are `aria-hidden`. They are decoration over video, and
  the tile's status region is unchanged.
- **FR-015** A text element's ink is the better of black and white against the worst-case
  composite:
  - **dark** if `contrastDark ≥ contrastLight`, where `contrastDark` is black ink against
    the *darker* of the sRGB and linear composites over black video, and `contrastLight` is
    white ink against the *lighter* of the two composites over white video;
  - computed per element from the eight hex digits, with no DOM read and no search;
  - at alpha `FF` it equals the opaque L > 0.1791 threshold.

  For `#FFFFFFD9` (the default) the style function instead emits exactly today's token
  strings (`var(--color-bg-label)` / `var(--color-fg-on-label)`). The guarantee is tested,
  not assumed:
  - **Shared vectors.** A fixture of colour → `{ legible, ink }`, generated by an
    independent reference script and committed with it, is read by the C# domain test and
    the TypeScript style test. It includes every example in this spec and the boundary
    pairs `#FFFFFF74`/`75` and `#D32F2FF8`/`F9`. Neither implementation generates its own
    expectations.
  - **Sampled guarantee.** Over the 8 cube corners and a 6×6×6 lattice, each at its minimum
    alpha and at `FF`, a contrast computed independently in the test, against both
    compositing models over white and black video, is ≥ 4.5 for the chosen ink. The
    minimum over the sample is asserted ≥ 4.5.
  - **Monotonicity.** For sampled colours, legibility never flips from true back to false as
    alpha rises. The minimum-alpha concept depends on this.
  - **Tokens.** `--color-bg-label` is pinned to white at 85%, and `--color-fg-on-label`'s
    worst-case contrast on it is asserted ≥ 4.5 (measured 12.7:1). This is what makes the
    `#FFFFFFD9` special case an equivalence.
- **FR-016** `OverlayEditor.tsx` is not modified. Its four guard files stay byte-identical.
- **FR-017** The PR quotes a measured `overlay_draw` p50/p95 at full cardinality with mixed
  kinds (9 tiles × 8 elements, half shapes), run twice, beside spec 225's baseline.
- **FR-018** The kiosk's `overlay_draw` measurement key includes each element's kind and
  colour as well as its text, so a republish that changes only a colour is measured and an
  unchanged re-render is not.
- **FR-019** The console dialog edits the **first `Text` element** of the set rather than
  index 0, and submits every other element unchanged. If the set has no `Text` element, the
  dialog says so, and offers rename and publish but no label editing. It must never coerce a
  shape into a label. *(PR-A.)*
- **FR-020** The frontend's `DEFAULT_OVERLAY_COLOR`, its hex pattern and its legibility
  threshold (4.5) are pinned equal to the domain's by a test that parses the C# source, the
  way `MAX_LABELS` is pinned today (`overlays.schema.test.ts:30`). The legibility
  *formula* is pinned by FR-015's shared vectors. This ships in PR-A, because the create
  form and the Zod schema need both.

  *(US4, PR-B)*: the dialog offers a colour input for the text element it edits:
  - a native `<input type="color">` for the hue (it has no alpha channel);
  - an **opacity slider whose minimum is that hue's computed minimum alpha**;
  - an eight-digit hex text field.

  The client upper-cases the value before sending.

---

## Locked tech choices

| Concern | Choice | Authority |
|---|---|---|
| Element model | One `OverlayElement` VO, `ElementKind` discriminator, nullable text component | **ADR-0165** (Accepted); ADR-0112 §2 (no identity) |
| Colour | `OverlayColor : IValueObject<string>`, canonical `#RRGGBBAA` upper-case (`#RRGGBB` accepted as `FF`); default `#FFFFFFD9` | **ADR-0165**; gate decisions 2026-10-04 |
| Alpha by kind | Box/Ellipse `00`–`FF`, no floor. Text: legible over worst-case video under both compositing models (per-colour floor, `75`–`FE`) | **ADR-0165** §2 |
| Text ink | Better of black/white against the worst-case composite (= L > 0.1791 when opaque); `#FFFFFFD9` keeps today's tokens | **ADR-0165**; WCAG 2.x relative luminance and contrast |
| Cap | `MaxElements = 8`, every kind counts | **ADR-0165** amending ADR-0164's scope, not its number |
| Persistence | Same owned collection, renamed table, `kind`/`color` columns, `CHECK` constraint | spec 150; `ux_overlay_revisions_one_published` belt-and-braces precedent |
| Migration | One EF migration via `MigrationRunner` | ADR-0067 |
| Contract | Clean `OverlayRevisionPublishedV3`; V2 deleted; resolved-text V2 kept | ADR-0073, ADR-0040, spec 150 FR-008 |
| Concurrency / idempotency | Unchanged: `If-Match` + EF token; `Idempotency-Key` on create | ADR-0113, ADR-0142 |
| Errors / guards | `Result<T, Error>` at the boundary; `Ensure.That` | ADR-0047, ADR-0105 |
| Frontend types | TS discriminated union over the flat wire shape; Zod `discriminatedUnion('kind')` | ADR-0079 |
| Tokens | The authored hue goes inline (data, validated at three layers). New semantic `--color-overlay-ink-dark` / `-light` over `--black` / `--white`, fixed across themes like `--color-bg-label` | ADR-0148, scoped by ADR-0165 |

---

## Latency budget impact (constitution §IV)

| Leg | Budget | Touched? |
|---|---|---|
| Camera → SFU | ≤ 80 ms | No |
| SFU → kiosk decode | ≤ 120 ms | No |
| Presentation buffer | ≤ 200 ms | No. Shapes ride the tile's one `useLabelDelay`; nothing new is buffered |
| Event → overlay state | ≤ 200 ms | **Rename only.** Same fan-out, same one event per overlay; shapes add `""` entries and no placeholders. Quote `NFR_VariableResolutionLatencyTests`' median, run twice, to show no movement |
| **Overlay composite + render** | **≤ 50 ms** | **Yes.** New node kinds, same node ceiling (72 per wall) |
| Headroom | ≤ 150 ms | Remainder |

**Composite + render.** The node ceiling does not move: ADR-0164's 72 holds because shapes
count toward the cap. What changes is what a node is. A box or ellipse is an absolutely
positioned element with a solid border and no text, no flex layout, no font, no
compositing layer, no shadow and no animation (FR-013). That should paint for less than the
text node it can replace. This leg is already over budget (ADR-0123 p50 54.2 ms; spec 225
CI mean p50 56.63 ms, σ 8.55, 21 runs), so "should" is not enough. **FR-017 requires the
figure.** The instrument is the same `reportKioskLatency('overlay_draw', …)` that spec 150
used, and the run is repeated because the first run after machine churn reads like a
regression.

§VII's dashboard obligation for this leg is still outstanding (#1940). This spec inherits
that position from specs 146 and 150 and does not discharge it. It is named here so the
reviewer sees it.

---

## Independent end-to-end test procedure

Against the run-mode Aspire stack, without reading test code.

1. Boot the stack. Mint a token with `sse.overlays.write` and `sse.overlays.read` from
   Aspire's **proxied** Keycloak endpoint.
2. `POST /overlays` with a name and five elements:
   - a `#d32f2f` `Box` at (0.1, 0.2, 0.4, 0.5);
   - a `#d32f2f` `Text` "Zone A" under it;
   - a `#FFEB3B` `Text` "Caution" at (0.6, 0.6, 0.3, 0.08);
   - a `#FFFFFF80` `Text` "Line 1" at (0.6, 0.75, 0.3, 0.08), translucent white above white's floor of `75`;
   - a `#FFA00080` `Ellipse` at (0.6, 0.2, 0.25, 0.25), a half-transparent stroke.

   Expect **201**.
3. `GET /overlays/{id}`. Expect `revisions[0].elements` with five entries in order, each
   with `kind` and an **eight-digit upper-case** `color` (`#D32F2FFF`, `#FFFFFF80`), the Box
   and Ellipse with `text: null` and `fontSizePx: null`. Expect no `labels` field.
4. Bad requests, each a separate `POST`:
   - a `Box` carrying `"text": "x"` → **400** naming `text`;
   - `"color": "red"` → **400** naming `color`;
   - a `Text` coloured `#D32F2F80` → **400** naming `color` and stating the minimum alpha `F9`;
   - a `Box` coloured `#D32F2F00` → **201**: an invisible stroke is allowed.
5. Publish revision 1 with the ETag from step 3 as `If-Match`. Expect **200**. In the Aspire
   dashboard, confirm **one** `OverlayRevisionPublishedV3`.
6. Bind the overlay to a tile of a published layout and open the kiosk wall. **Expect a red
   rectangle outline, a red caption chip with white text below it, a yellow chip with black
   text, a see-through white chip with black text, and a half-transparent orange ellipse
   outline**, at their positions, with no pulse.
7. Open a tile bound to an overlay published **before** this feature. **Expect one label
   that looks exactly as it did.**
8. In the console, open step 2's overlay as a new draft (branch). Change the caption's text
   and save. `GET` it: **the Box and Ellipse are unchanged and in place.**
9. Read `overlay_draw` p50/p95 on the nine-tile mixed fixture. Record them beside spec 225's
   baseline. Repeat once and record both runs.

---

## Colour: what is decided and what is not

**Closed by the human at the gates (2026-10-04), all final:**
1. colour is arbitrary, not a palette;
2. strokes have no readability floor;
3. colour carries alpha.

ADR-0165 is **Accepted**. These follow from those decisions by architectural reasoning,
and are made here:
- hex over RGB(A) components;
- canonical eight-digit upper-case form, with six digits accepted as opaque;
- default `#FFFFFFD9`;
- the worst-case, compositor-independent legibility rule and the per-colour text alpha floor
  it implies;
- computed black/white ink;
- the `#FFFFFFD9` token special case;
- the coarse `75` floor in the database.

**The text alpha floor is not a product parameter.** It follows from the 4.5:1 ratio
already promised and the reading "readable over any video". No fixed percentage can carry
that guarantee below 100% (table in §"Why the text floor is per-colour"), so the floor is
per colour: `75` (45.9%) for white, `D1` (82%) for black, up to `FE` (99.6%) for mid-tones.

**Nothing in the colour design is an open human call.** One consequence the product owner
should know about, which is not an open question: **mid-tone text colours can barely be
made translucent.** A mid-grey caption needs ~99% opacity and the spec's red needs 97.6%,
because that is what 4.5:1 over arbitrary video costs. Relaxing it would mean choosing a
weaker ratio, or a "typical video" assumption instead of the worst case. Either would
reopen FR-015's guarantee, and is a decision this spec does not make by default.

---

## Follow-on work this spec creates (file before PR-A merges)

1. **Icon / glyph element**, gated on its own ADR (icon set, licence, subsetting,
   self-hosting, external-host architecture test), modelled on ADR-0147.
2. **Rule / divider element**, gated on a geometry decision (orientation, axis, diagonals).
3. **A comment on #2343**: the multi-element editor should author kind and colour, and must
   replace FR-019's first-text-element rule.
4. **Box fill / stroke style**, only if an operator asks.
