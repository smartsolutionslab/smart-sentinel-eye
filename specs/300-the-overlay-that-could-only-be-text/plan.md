# Plan 300: The overlay that could only be text

Implements `spec.md`. The precedent is **spec 150**, which made the revision payload an
ordered owned collection. This plan changes what a member of that collection *is*. Where a
mechanism is unchanged from spec 150 (ordinal, deep copy on branch, set validation two-tier,
one-version-per-overlay resolution), this plan cites it instead of repeating it.

**Governed by ADR-0165 (Accepted 2026-10-04)**
(`docs/adr/0165-an-overlay-element-is-one-value-object-with-a-kind-and-a-colour.md`). It
covers the element model, arbitrary colour with alpha, the text legibility rule, computed
ink, and the cap's reach.

**Revised 2026-10-04 at the gates.** Colour is arbitrary, not the five-entry palette first
drafted, and it carries alpha (`#RRGGBBAA`). A `Text` colour must be legible over
worst-case video; strokes take any alpha. Only the colour sections below changed.

---

## Bounded contexts touched

The boundary rule holds throughout: no cross-context project reference, and all traffic
goes through `Shared.Contracts` (NetArchTest).

| Context | What changes | Layers |
|---|---|---|
| **OverlayDesigner** | `Label` → `OverlayElement` with kind + colour; set rules renamed; migration | Domain, Application, Infrastructure, Api |
| **Shared.Contracts** | `OverlayRevisionPublishedV3` + `OverlayElementV3`; V2 deleted. `ResolvedOverlayTextChangedV2` untouched | — |
| **SystemVariables** | V3 handler; seeder reads `"elements"`; non-text → `""` | Application, Infrastructure |
| **LayoutComposition** | V3 handler relays elements on the hub message | Application, Infrastructure |
| **AuditObservability** | `IntegrationEventAuditHandler` V3 overload; check `V1ResourceMap` convention covers V3 | Application |
| **ScenarioSimulator** | Seeding body: `elements` with `kind: Text`, `color: #FFFFFFD9` | — |
| **Frontend** (`apps/shared`, `kiosk-web`, `management-web`) | Wire types, ink tokens + ink rule, wall render, dialog safety (PR-A); dialog colour input (PR-B) | — |

No new Aspire resource, container, queue or Keycloak change. **No infra engineer is
needed.**

---

## OverlayDesigner: Domain

### `OverlayElement` replaces `Label`

File `Domain/Overlay/OverlayElement.cs` replaces `Label.cs`. It is still a `sealed record …
: IValueObject`, in the per-aggregate folder (ADR-0092).

```
OverlayElement : IValueObject
  ElementKind         Kind
  NormalizedPosition  Position
  NormalizedSize      Size
  OverlayColor        Color
  TextContent?        Text          // present ⇔ Kind == Text
  private int ordinal;  ElementOrdinal Ordinal   // spec 150's LabelOrdinal, renamed

  const int MaxElements = 8         // was Label.MaxLabels (ADR-0164 → ADR-0165)

  static OverlayElement TextElement(string text, int fontSizePx, NormalizedPosition, NormalizedSize, OverlayColor)
  static OverlayElement Box(NormalizedPosition, NormalizedSize, OverlayColor)
  static OverlayElement Ellipse(NormalizedPosition, NormalizedSize, OverlayColor)
  internal OverlayElement AtOrdinal(int)          // unchanged mechanism
```

`TextContent(string Value, int FontSizePx) : IValueObject` carries today's
`Label.From` guards verbatim: non-blank, ≤ `MaximumTextLength` = 256, trimmed, font size
8..256. The constants move onto `TextContent`. `string`/`int` stay legal there as a value
object's own backing values (§II exemption 3).

**Why a nullable component, not two nullable scalars on the element:** the presence rule
becomes one reference check (`Text is not null` ⇔ `Kind == Text`), not two that could
disagree. EF maps a nullable owned reference, which it does not do for `Option<T>`.
CLAUDE.md's ADR-0141 note says persisted absences use nullable value-object references.

**`ElementKind`** follows `OverlayRevisionState`'s closed-set shape exactly
(`OverlayRevisionState.cs`): `sealed record ElementKind(string Value) :
IValueObject<string>`, static `Text` / `Box` / `Ellipse` instances, and a `From` switch that
throws `ArgumentException` on an unknown value.

**`OverlayColor`** is an open value under the same marker:

```
public sealed record OverlayColor : IValueObject<string>
  public string Value { get; }                     // always "#RRGGBBAA", upper-case
  public const string DefaultValue = "#FFFFFFD9";  // pinned by the frontend test (FR-020)
  public const string Pattern = "^#[0-9A-Fa-f]{6}([0-9A-Fa-f]{2})?$";
  public static OverlayColor Default { get; }      // From(DefaultValue)
  public static OverlayColor From(string value)
      // Ensure.That(value, nameof(value)).IsNotNullOrWhiteSpace();
      // a pattern mismatch throws ArgumentException naming the parameter;
      // appends "FF" to a six-digit value, then ToUpperInvariant()
```

It uses a private constructor, so `From` is the only way in and the eight-digit upper-case
canonical form is an invariant, not a convention. Prefer a source-generated
`[GeneratedRegex]` for the pattern, matching any existing use in the solution; check before
introducing it. `OverlayColor` itself is **kind-agnostic**: any well-formed value is a
valid colour. Legibility is a rule about a *text element*, so it lives with the text
factory, not the colour.

### `TextLegibility`: the domain's half of the legibility rule

A pure static domain service in `Domain/Overlay/TextLegibility.cs`:

```
public static class TextLegibility
  public const double MinimumContrast = 4.5;          // pinned by the frontend test (FR-020)
  public static bool IsLegible(OverlayColor color)    // max(contrastLight, contrastDark) >= 4.5
  public static byte MinimumAlpha(OverlayColor color) // the lowest AA that would be legible at this RGB; for the 400's message
```

The formula, written once here and once in TypeScript, and pinned by the shared vectors:
1. Parse R, G, B, A from the eight digits into the 0–1 range. Linearise R, G, B
   (`c <= 0.04045 ? c/12.92 : ((c+0.055)/1.055)^2.4`). `Ls` is the WCAG luminance of the
   opaque colour.
2. **Lighter composite over white video** = `max(lum(A·rgb + (1−A)), A·Ls + (1−A))`. The
   first term is sRGB-encoded compositing, the second linear-light.
   **Darker composite over black video** = `min(lum(A·rgb), A·Ls)`.
3. `contrastLight = 1.05 / (lighter + 0.05)` and `contrastDark = (darker + 0.05) / 0.05`.
4. Legible ⇔ `max(contrastLight, contrastDark) ≥ 4.5`.

`MinimumAlpha` scans A = 0..255 and returns the first legible value. The worst case rises
monotonically with alpha (checked over the cube), so the first legible value is the floor,
and A = 255 is always legible (exhaustive minimum 4.5826). It runs only when building the
error, at most 256 cheap evaluations, never on a hot path.

**Why the domain now computes luminance, against the previous revision.** That revision
kept luminance out of the domain because ink was purely presentation. With alpha,
**legibility becomes an invariant of what the system may hold**: an unreadable caption must
be refused at write time, not painted and regretted. Ink selection stays in the frontend,
which is still the presentation half. `double` in this service is computed, not state, so
§II does not bind it (ADR-0140).

`TextElement(...)` calls `TextLegibility.IsLegible(color)`. When it fails, it throws
`ArgumentException(paramName: "color")` with the message `"A text surface coloured {RGB}
needs alpha of at least {MinimumAlpha:X2} to stay readable over any video."`. The
endpoint's existing `catch (ArgumentException)` turns that into a 400 naming `color`.

### Invariants

Per element, enforced by the three factories, which are the only public constructors:

| Kind | Text component | Colour | Geometry |
|---|---|---|---|
| Text | required | required, **and `TextLegibility.IsLegible`** | shared rule |
| Box | **must be absent** | required, any alpha `00`–`FF` | shared rule |
| Ellipse | **must be absent** | required, any alpha `00`–`FF` | shared rule |

The EF materialisation constructor (spec 150's `private Label(string, int)` pattern) cannot
route through a factory. A private `RequireConsistent()` therefore runs from the factories
and is asserted by a test that round-trips every kind through the real EF stack (T014). The
database `CHECK` constraint (below) is the backstop for rows written any other way.

Set level: `LabelSetViolation` → **`ElementSetViolation { Empty, TooMany }`**, and
`Overlay.ValidateLabels` → **`ValidateElements`**, same two tiers (operator `Result` 400 +
private `RequireValidElements` backstop). `TooMany` counts every kind (ADR-0165). **No
per-kind rule exists at set level.** A box-only overlay is valid, and so is a text-only one.

### `Revision`, `Overlay`, the domain event

The renames are mechanical: `Labels` → `Elements`, `ReplaceLabels` → `ReplaceElements`,
`OverlayRevisionPublishedDomainEvent.Labels` → `Elements`. `NewDraft`'s per-element deep
copy (spec 150 FR-007, the highest-risk line in that spec) now also copies `Color` and the
nullable `Text` component. **An owned reference shared between two revisions re-keys and
throws**, which is exactly the failure spec 150 documented, one level further down. Publish,
Revert, Archive, RecomputeArchival and the at-most-one-Published rule **must not appear in
the diff**.

---

## OverlayDesigner: Infrastructure

### Mapping

`revisions.OwnsMany(revision => revision.Labels, …)` → `OwnsMany(revision =>
revision.Elements, …)` on **`overlay_revision_elements`**, key `(revision_id, ordinal)`,
unchanged. `kind` and `color` are converted scalars (`HasConversion(k => k.Value,
ElementKind.From)`; `kind` max length 16, `color` max length 9). `Text` becomes `OwnsOne(element => element.Text, …)`
**without** `Navigation(...).IsRequired()`. It is the one optional owned reference in this
configuration, and the #2022 comment on `Position`/`Size` must be amended to say why this
one differs. Its two columns are nullable.

`OverlayConfiguration` is near ADR-0084's 300-line advisory limit (213 today) and gains
lines. If it crosses 300, extract the element mapping into a private static method or a
nested builder class. Do not suppress the analyzer.

### Migration (ADR-0067, one migration)

On top of `20261004121044_AddOverlayRevisionLabels`:

1. `RenameTable overlay_revision_labels → overlay_revision_elements`.
2. Rename columns: `label_x → x`, `label_y → y`, `label_width → width`, `label_height →
   height`, `label_text → text`, `label_font_size_px → font_size_px`.
3. `AlterColumn text, font_size_px → nullable`.
4. `AddColumn kind varchar(16) NOT NULL DEFAULT 'Text'`, `AddColumn color varchar(9) NOT
   NULL DEFAULT '#FFFFFFD9'`. Postgres fills existing rows from the default, which **is**
   the backfill. `#FFFFFFD9` is white at 217/255 = 85.1%, the nearest 8-bit alpha to
   `--color-bg-label` (`color-mix(in oklab, var(--white) 85%, transparent)`). It is the
   value for which the style function emits today's tokens, so every existing label renders
   unchanged. Then drop both defaults, so no future write gets a silent kind or colour
   (FR-003).
5. `CHECK`s, the belt-and-braces role `ux_overlay_revisions_one_published` already plays
   for the aggregate's lifecycle rule:
   - `ck_overlay_revision_elements_text_matches_kind`: `(kind = 'Text') = (text IS NOT NULL
     AND font_size_px IS NOT NULL)`;
   - `kind IN ('Text','Box','Ellipse')`;
   - `color ~ '^#[0-9A-F]{8}$'`. **Upper-case only**: the domain canonicalises, so a
     lower-case or six-digit row can only come from a write that bypassed the aggregate;
   - **`kind <> 'Text' OR substring(color from 8 for 2) COLLATE "C" >= '75'`**. This is the
     coarse text floor: `75` is the lowest alpha at which *any* text colour (white) is
     legible, so the check is a necessary condition and never rejects a valid row.
     Two-digit upper-case hex compares in numeric order under the C collation, and the
     explicit `COLLATE "C"` keeps that independent of the database's locale. The exact
     per-colour rule is **not** repeated in SQL: a third implementation of nine `pow`s,
     outside the shared vectors, is how the three would drift.

Every step is a metadata change or a constant default. There is no row rewrite and no read
window. The `CHECK` is validated against existing rows, which all satisfy it after step 4.
**Red test (T010):** a row inserted in the pre-migration shape reads back as one `Text` /
`#FFFFFFD9` element with its geometry and text intact.

---

## OverlayDesigner: Application and Api

| Type | Change |
|---|---|
| `CreateOverlayDraftCommand`, `EditDraftRevisionCommand` | `IReadOnlyList<Label> Labels` → `IReadOnlyList<OverlayElement> Elements` |
| `OverlayLabelDto` | → `OverlayElementDto(string Kind, string Color, decimal NormalizedX, …Y, …Width, …Height, string? Text, int? FontSizePx)` |
| `OverlayRevisionDto`, `PublishedOverlayDto` | `Labels` → `Elements` |
| `LabelRequest` | → `ElementRequest(string Kind, string Color, decimal NormalizedX, …, string? Text, int? FontSizePx)` |
| `CreateOverlayRequest`, `EditDraftRequest` | `Labels` → `Elements` |
| `*Errors.cs` | `OVERLAY_LABELS_EMPTY / _TOO_MANY` → `OVERLAY_ELEMENTS_EMPTY / _TOO_MANY` |

`OverlayEndpoints.Commands.cs`'s `ParseLabel` → `ParseElement`, a switch on
`ElementKind.From(request.Kind)` calling the matching factory. It stays **inside the one
existing `try`**, so the first bad element 400s the whole set, naming its field through the
existing `OVERLAY_INVALID_INPUT` path (spec 150's "one bad label rejects the set", unchanged
in mechanism). The factory's guard names the parameter (`text`, `fontSizePx`, `color`,
`kind`), and that name is what the problem detail carries. **Verify each of the six
bad-request examples names the field the spec says**. This is the kind of claim that reads
true and isn't.

Routes, scopes (`sse.overlays.read` / `.write`), `If-Match`, `Idempotency-Key`: **no
changes.**

---

## Shared.Contracts

```
OverlayRevisionPublishedV3(
    Guid Overlay, int RevisionNumber, string Name,
    IReadOnlyList<OverlayElementV3> Elements,
    DateTimeOffset PublishedAt, Guid PublishedBy, EventMetadata Metadata) : IIntegrationEvent

OverlayElementV3(
    string Kind, string Color,
    decimal NormalizedX, decimal NormalizedY, decimal NormalizedWidth, decimal NormalizedHeight,
    string? Text, int? FontSizePx)
```

Primitives only (ADR-0040). Delete `OverlayRevisionPublishedV2.cs` in the same commit,
following spec 150's V1 → V2 precedent. `ResolvedOverlayTextChangedV2`: **no field change**.
Amend only its XML doc to say the list is index-aligned with `Elements`, with `""` for a
non-text element (FR-009).

Consumers, enumerated by `grep -rln "OverlayRevisionPublishedV2\|OverlayLabelV2"` over
`src`, `tests` and `apps` on 2026-10-04. Re-run the grep at T007; this list is a floor, not
a ceiling:

- **AuditObservability:** `IntegrationEventAuditHandler.cs:49` overload. `V1ResourceMap`
  carries no hand-tweak for the published event today (only `ResolvedOverlayTextChangedV2`,
  `Conventions.cs:74`). **Confirm the convention resolves V3's `Overlay` to
  `DomainResourceKind.Overlay` with a `V1ResourceMapTests` case** rather than assuming it.
- **LayoutComposition:** `OverlayRevisionPublishedV2Handler`, its registration in
  `LayoutCompositionInfrastructureModule`, `<see cref>`s in `OverlayRevisionArchivedV1Handler`
  and `WallSceneChangedV1Handler`.
- **SystemVariables:** `OverlayRevisionPublishedV2Handler`, `GetOverlaySnapshotErrors.cs`
  doc, `Log.cs`, `ReverseIndexSeederHostedService`, `OverlayRevisionArchivedV1Handler` doc.
- **OverlayDesigner:** `OverlayRevisionPublishedDomainEventHandler` (emitter).
- **Frontend:** `apps/shared/src/realtime/layoutHub.ts` (message types).
- **Tests:** `Shared.Contracts.Tests/OverlayRevisionPublishedV2Tests.cs`,
  `LayoutComposition.Application.Tests/…/OverlayRevisionPublishedV2HandlerTests.cs`,
  `SystemVariables.Application.Tests/…/OverlayRevisionPublishedV2HandlerTests.cs`,
  `EventMetadataFabDeclarationTests`, and the integration tests that build bodies through
  `tests/Integration.Tests/Fixtures/OverlayRequests.cs` (the request shape changes there
  once).

---

## SystemVariables: two silent-break sites

1. **`OverlayRevisionPublishedV3Handler`:** `elements.Select(e => e.Text ?? "")`. A box
   registers no placeholder, and the list stays index-aligned.
2. **`ReverseIndexSeederHostedService`** parses `GET /overlays?state=Published` as raw
   JSON. Today it reads `"labels"` and each element's `"text"`, contributing `""` when
   absent (`:135-145`). **Renaming the DTO field to `elements` compiles and then seeds zero
   overlays on every cold start.** That is spec 150's D4 exactly, and it will recur unless
   it is guarded. Read `"elements"`. A JSON `null` `text` (a box) already yields `""` via
   `GetString() ?? string.Empty`. **Red test first, with a counterfactual:** a
   `ReverseIndexSeederHostedServiceTests` payload in the new shape (Box then Text) seeds
   `["", "<text>"]`. Against the unchanged seeder the same payload seeds **zero overlays**.
   Quote both.

`IReverseIndex`, `IOverlayTextVersions`, the two variable-change publishers and
`GetOverlaySnapshotQueryHandler` resolve "every text in the list" already. A `""` resolves
to `""`. **No change expected. Confirm by test, do not assume**: add one case to the
value-changed handler test with a Box at ordinal 0.

---

## LayoutComposition: relay only

`OverlayRevisionPublishedHubMessage` carries the elements list. The per-fab fan-out loop is
unchanged. The handler destructures first. `HandlerDeconstructionTests` fails the build if a
local is named after a different field, so rename `labels` → `elements` deliberately.

---

## Frontend

### `apps/shared/src/api/overlays.api.ts` and `overlays.schema.ts`

```ts
type ElementBase = { color: string /* '#RRGGBBAA' */; normalizedX; normalizedY; normalizedWidth; normalizedHeight };
export type OverlayLabel   = ElementBase & { kind: 'Text'; text: string; fontSizePx: number };
export type OverlayShape   = ElementBase & { kind: 'Box' | 'Ellipse'; text?: null; fontSizePx?: null };
export type OverlayElement = OverlayLabel | OverlayShape;
```

`OverlayRevision.labels` → `elements: OverlayElement[]`, and the same for
`PublishedOverlay`, the create input and the edit body. **`OverlayLabel` keeps its name as
the text variant**, so `OverlayEditor.tsx` (props `value: OverlayLabel`) compiles untouched
(FR-016). Its `value` gains `kind` and `color`, which it ignores except through the style
function.

Zod: `overlayElementSchema = z.discriminatedUnion('kind', [textSchema, boxSchema,
ellipseSchema])`:
- every kind: `color: z.string().regex(OVERLAY_COLOR_PATTERN).transform(canonicalColor)`,
  where `canonicalColor` appends `FF` to six digits and upper-cases;
- `textSchema` adds `.superRefine` with the shared `isLegibleTextColor(color)`. It reports
  `Needs opacity of at least ${minimumTextAlpha(rgb)}` on `color`, so the form says what the
  server would say.

`MAX_LABELS` → `MAX_ELEMENTS`. New exports: `DEFAULT_OVERLAY_COLOR = '#FFFFFFD9'`,
`OVERLAY_COLOR_PATTERN`, and `MINIMUM_TEXT_CONTRAST = 4.5`.

**`overlays.schema.test.ts:16` reads `Label.cs` by path and parses `MaxLabels`.** The file
rename breaks that test loudly, which is good. Repoint it at `OverlayElement.cs` /
`MaxElements`. Add the same source-parse pins (FR-020) for:
- `OverlayColor.DefaultValue` == `DEFAULT_OVERLAY_COLOR`;
- `OverlayColor.Pattern` == `OVERLAY_COLOR_PATTERN.source`;
- `TextLegibility.MinimumContrast` == `MINIMUM_TEXT_CONTRAST`;
- the `ElementKind` value set == the union's kinds.

The legibility *formula* cannot be pinned by parsing source. It is pinned by the shared
vectors (below).

### Ink tokens (`apps/shared/src/ui/tokens/tokens.css`)

The authored colour never becomes a token: it is data, and there is no fixed set to name.
Two semantic tokens are added for the ink the rule picks, defined once and **not**
redefined per theme, the way `--color-bg-label` is today (`:109-110`, absent from the light
block). An overlay sits on video, not on a theme surface:

```
--color-overlay-ink-dark:  var(--black);   /* on a light surface */
--color-overlay-ink-light: var(--white);   /* on a dark surface  */
```

`DesignTokenLayerTests` must stay green: both cite primitives only. **The inks must stay
pure black and pure white.** Every floor in spec §"Why the text floor is per-colour" depends
on the extremes. A comment at the tokens says so, so nobody "softens" them to gray-900, and
the shared-vector test fails if they are.

### `apps/shared/src/ui/composites/textLegibility.ts` (new)

The frontend half of the rule, identical in formula to the domain's `TextLegibility` (plan
§"TextLegibility") and pinned to it by the vectors. All functions are pure and DOM-free:
- `parseColor(hex) → { r, g, b, a }` in the 0–1 range;
- `worstCaseContrasts(color) → { light, dark }`. Light is the lighter composite over white
  video, dark the darker over black, each taken across both sRGB-encoded and linear-light
  compositing;
- `inkFor(color) → 'dark' | 'light'`: dark if `dark ≥ light`;
- `isLegibleTextColor(color) → boolean`: `max(light, dark) ≥ MINIMUM_TEXT_CONTRAST`;
- `minimumTextAlpha(rgbHex) → string` (two hex digits): scans 00..FF. Used only by the form
  error and PR-B's slider minimum, never per paint.

Per paint, `inkFor` is three luminance evaluations (nine `pow`), with no loop and no
`getComputedStyle`. At alpha `FF` it equals the opaque L > 0.1791 threshold. A comment
carries the derivation and a pointer to ADR-0165 §2.

**The shared vectors.** `tests/OverlayDesigner.Domain.Tests/Overlay/text-legibility-vectors.json`:
an array of `{ color, legible, ink, minimumAlpha }`. It is generated by
`text-legibility-vectors.mjs`, an independent reference script committed beside it, which
is neither implementation. It covers:
- every colour in the spec's scenarios;
- the boundary pairs `#FFFFFF74`/`75` and `#D32F2FF8`/`F9`;
- the opaque threshold pair `#757575FF` / `#767676FF`;
- the 8 cube corners at `FF` and at their minimum alpha;
- 64 lattice colours at their minimum alpha and one step below.

The C# test (`TextLegibilityVectorTests`) and the TS test (`textLegibility.test.ts`) both
read it, the TS one by relative path, the mechanism `overlays.schema.test.ts` already uses
for `Label.cs`. A drift in either implementation fails its own test against the same
expectations.

### `overlayLabelStyle.ts`

Two pure, DOM-free, allocation-only functions:
- **`overlayLabelSurfaceStyle({ fontSizePx, color })`.** If `color ===
  DEFAULT_OVERLAY_COLOR`, return **exactly today's object**, with the same token strings in
  the same keys (`var(--color-bg-label)`, `var(--color-fg-on-label)`). That is what keeps
  `OverlayLabelCharacterisation` (`:78`, `:88`) and `OverlayLabelParity` passing with no
  edited assertion. Otherwise return the same object with `background: ${color}` (eight-digit
  hex is valid CSS, so no `color-mix`) and `color: var(--color-overlay-ink-${inkFor(color)})`.
- **`overlayShapeStyle({ kind, color })`** returns `border: max(2px, 0.25cqw) solid
  ${color}`, `borderRadius: kind === 'Ellipse' ? '50%' : 0`, `background: transparent`,
  `boxSizing: border-box`. There are no shadows, transitions or animations (FR-013). Stroke
  width is in `cqw`, so it scales with the tile, as spec 294 made type do. Any alpha,
  including `00`.

**Trust boundary (§VIII).** The colour reaches an inline style from a hub message. React
style objects are not an HTML injection path, and the domain, the `CHECK` and the schema
all enforce the pattern. The style functions do **not** re-validate: validation happens at
the boundary that receives input, not at every reader (CLAUDE.md, "validate at trust
boundaries only"). A malformed value that got past three layers renders as an invalid CSS
value, which the browser drops. It does not render as an injection. The kiosk likewise does
not re-check legibility. A text colour on the wire was legible when written, and the rule
does not change after write.

**Tests (FR-015).** In `textLegibility.test.ts`:
- the shared vectors;
- the sampled guarantee: 8 corners + a 6×6×6 lattice, each at its minimum alpha and at
  `FF`, with contrast computed **independently in the test** across both compositing models
  over white and black video, not by calling back into the function under test. The chosen
  ink must reach ≥ 4.5, and the sample minimum is asserted ≥ 4.5;
- monotonicity: for sampled colours, legibility never goes from true back to false as alpha
  rises.

In `overlayLabelStyle.test.ts` (also a characterisation guard, so cases are added, never
edited):
- `#D32F2FFF` → `background: #D32F2FFF` and the light ink;
- `#FFEB3BFF` → the dark ink;
- `#FFFFFFD9` → today's object, deep-equal.

A token test pins `--color-bg-label` to `color-mix(in oklab, var(--white) 85%,
transparent)`, and `--color-overlay-ink-dark/-light` to `var(--black)` / `var(--white)`.

### `CameraViewer.tsx`

`CameraViewerOverlay` becomes a union mirroring the API (`kind`, `color`, geometry, and
`text`/`fontSizePx` for Text). The `.map()` at `:407` switches on `kind`: `Text` →
today's `OverlayLabel` span, unchanged apart from the style call receiving colour;
`Box`/`Ellipse` → a new `OverlayShape` span with `data-testid="camera-viewer-overlay-shape"`,
`data-kind`, `aria-hidden="true"`, the same four percentage placement properties and
`pointerEvents: 'none'`. Both stay **direct children** of the aspect-ratio box, with no
wrapper (spec 150 FR-012, guarded by its `OverlayLabelNodeCountCharacterisation`). The key
stays the ordinal.

**Shapes get a separate testid on purpose.** The existing e2e and unit guards count
`camera-viewer-overlay-label` nodes, and a box must not change those counts on fixtures
that have no boxes. Keeping the label testid meaning "a text label" means those guards stay
unedited.

### `apps/kiosk-web/src/features/cell/LayoutGrid.tsx`

`labels` → `elements` at `:386-464`. Two meanings to state:

- `hasPlaceholder` / `labelTextKnown` consider **`Text` elements only**. `labelTextKnown`
  is true when every Text element's `text` is a string. A shape has no text to know.
- `renderLabels` → `renderElements`, carrying `kind` and `color` through.
  `resolvedTexts[index]` applies to Text elements. Shapes ignore it, and positional
  alignment is preserved because shapes hold `""` slots (FR-009).
- **`renderLabelsKey` (`:474`) joins text only.** Extend it to `kind|color|text` per
  element (FR-018), still a scalar, still `\u0000`-joined. A colour-only republish then
  re-measures, and an equal re-render still does not (#1888/#1889, ADR-0123). Red test.

`useLabelDelay` is still called once per tile with the texts. Shapes are not delayed
separately, because they are static between publishes, and a publish replaces the whole
set.

### `apps/kiosk-web/src/features/cell/useOverlayHubHandlers.ts`, `apps/shared/src/realtime/layoutHub.ts`

The published message type moves to `elements`. `resolvedTexts` handling is unchanged.

### `apps/management-web`: FR-019 (PR-A) and US4 (PR-B)

**PR-A, safety.** `OverlayEditorDialog.tsx` binds `labels.0` (`:327`) and watches
`labels.0.text` (`:176`). After the cut, the edited index is **`textIndex =
elements.findIndex(e => e.kind === 'Text')`**:

- `textIndex >= 0`: bind `elements.${textIndex}`. The PATCH sends all elements (already
  true since spec 150 FR-018, `:216`).
- `textIndex === -1`: render a notice ("This overlay has only shapes. Edit them through the
  API until the editor supports shapes (#2343)"). The name field still works. Save sends the
  elements unchanged.
- Create path: `DEFAULT_INPUT` is one `{ kind: 'Text', color: DEFAULT_OVERLAY_COLOR, … }`. The e2e
  wall seeders (`e2e/support/seed-*.setup.ts`) create through this path and must keep
  working unedited.

`OverlaysPage.tsx`: `labelsOf` → `elementsOf`; the row summary (`:155`) shows the first
Text element's text, or "Shapes only" when there is none.

**Red tests (in `OverlayEditorDialog.test.tsx`):** (a) target `[Box, Text]` → the editor
shows the Text, and save sends `[Box(unchanged), Text(edited)]`; (b) target `[Box]` → the
notice shows and save sends `[Box]` unchanged.

**PR-B, US4.** Three controls, composed into one RHF field `elements.${textIndex}.color`
and validated by the same Zod schema (pattern + legibility):
- a native `<input type="color">` for the hue (it reads and writes `#rrggbb` and has no
  alpha; Radix has no colour primitive);
- an **opacity slider whose `min` is `minimumTextAlpha(hue)`**, so the author cannot drag
  into an illegible value, and the floor visibly moves as the hue changes;
- an eight-digit hex text field for direct entry.

The editor preview shows the surface and computed ink through `overlayLabelSurfaceStyle`,
with no edit to `OverlayEditor.tsx`. Red tests:
- picking `#d32f2f` at full opacity sends `color: '#D32F2FFF'` for that element only, with
  the others byte-identical;
- the slider's minimum for `#d32f2f` is `F9` and for `#ffffff` is `75`;
- typing `#D32F2F80` into the hex field shows "Needs opacity of at least F9" and does not
  submit;
- typing `red` shows the pattern error.

### ScenarioSimulator

`OverlayDesignerClient.CreateOverlayBody` sends `elements: [{ kind: "Text", color:
"#FFFFFFD9", … }]`. Seeded scenarios stay single-text.

### e2e

- `e2e/overlays.spec.ts`: the read paths `revisions[0].labels[0].…` → `.elements[0].…`.
  **Expected values unchanged**. An edited expected value is a block (spec 150 T026's rule).
- `kiosk-shows-a-label-over-video.spec.ts`, `kiosk-shows-two-labels-over-video.spec.ts`,
  `kiosk-label-scales-with-its-tile.spec.ts`: seeding bodies that POST `labels` directly
  move to `elements` with `kind`/`color`. **Assertions unchanged.** Re-read each file at
  T020 to see which ones seed by API and which by UI.
- New `kiosk-shows-shapes-over-video.spec.ts` (T022) needs its shard-filter entry.

---

## Phase 4a: colour per artefact

### CHARACTERISATION: green before, unmodified after

| Guard | Rule |
|---|---|
| `OverlayLabelCharacterisation`, `OverlayLabelParity`, `OverlayLabelNodeCountCharacterisation`, `overlayLabelStyle.test.ts` | Only prop-construction lines may change (adding `kind: 'Text', color: '#FFFFFFD9'`). New cases may be **added** to `overlayLabelStyle.test.ts`; existing ones may not be edited. **An edited assertion is a block.** This is what proves the default colour is today's look. |
| `OverlayEditorCharacterisation`, `OverlayEditorBackdrop`, `OverlayEditorKeyboard`, `OverlayEditorUndo` | **Byte-identical.** Any diff is a scope breach (FR-016). |
| `OverlayRevisionStateMachineTests`, `OverlayChainArchivalTests`, `TimestampOrderingTests` | Only `Label.From(...)` call sites change to `OverlayElement.TextElement(...)`. Lifecycle assertions are untouched. |
| `OverlayLabelSetTests` → renamed `OverlayElementSetTests` | Empty / TooMany / boundary-at-8 assertions unchanged in value. This proves the cap did not move. |
| `kiosk-shows-a-label-over-video.spec.ts`, `kiosk-shows-two-labels-over-video.spec.ts`, `overlays.spec.ts` | Seeding/read paths only; expected values and assertions unchanged. |

The rename is the behaviour-preserving half. It must not carry a behaviour change in the
same commit. Commit the C#-internal rename (OverlayDesigner types and members only, with no
HTTP field, error code or table change) separately from the kind/colour semantics and the
wire cut, and make each commit build on its own (CLAUDE.md, stacked commits).

### RED: observed failing, output quoted in the PR

- `ElementKind.From` rejects unknown kinds. `OverlayColor.From` accepts `#d32f2f` and
  returns `#D32F2FFF`, accepts `#ff000080` and returns `#FF000080`, and rejects `red`,
  `#F00`, `#F00F`, `#FF00`, `#FF0000A`, `#FF0000AAB`, `#GG0000`, `""` and `null`. The three
  factories enforce the presence table, including Box with text and Text without.
- `TextLegibility` against the shared vectors (the C# half). `TextElement` refuses
  `#FFFFFF74` and `#D32F2FF8`, accepts `#FFFFFF75` and `#D32F2FF9`, and its exception names
  `color` and carries `F9` for `#D32F2F80`. `Box` / `Ellipse` accept `#D32F2F00`.
- A set of 5 boxes + 4 texts is `TooMany` (shapes count).
- Branch deep-copies `Color` and the nullable `Text` (integration, real EF; the FR-007 trap
  one level down).
- Migration: a pre-migration row reads back `Text`/`#FFFFFFD9`. The `CHECK` rejects raw
  `INSERT`s of a Box with text, of a lower-case colour, of a six-digit colour, and of a
  `Text` coloured `#FFFFFF74` (below the coarse floor). It accepts a `Box` coloured
  `#FFFFFF00`.
- API: every bad-request example in the spec's outline, each naming its field.
- `OverlayRevisionPublishedV3` carries kind and colour; exactly one per publish.
- Seeder reads `"elements"` (with counterfactual).
- The TS half of the shared vectors, plus the sampled-guarantee and monotonicity tests
  (§"textLegibility.ts"). Pin tests for `MAX_ELEMENTS`, the kinds, the default colour, the
  colour pattern and the 4.5 threshold against the C# source.
- `CameraViewer`: Box → bordered unfilled node, Ellipse → `borderRadius: 50%`, both
  `aria-hidden`, both direct children, no `animation`/`boxShadow` in computed style.
- `overlayLabelSurfaceStyle` with `#D32F2FFF` → `background: #D32F2FFF` and
  `var(--color-overlay-ink-light)`; with `#FFEB3BFF` → `var(--color-overlay-ink-dark)`;
  with `#FFFFFF75` → `var(--color-overlay-ink-dark)`; with `#FFFFFFD9` → today's object,
  deep-equal.
- `LayoutGrid`: a colour-only change fires `measureOverlayDraw` once; an equal re-render
  does not.
- Dialog FR-019 (a) and (b). PR-B: colour input.
- e2e: Box + Text + Ellipse visible on the wall.

The 409/403/idempotency scenarios run unchanged code paths. They are regression cover, not
new-behaviour red.

---

## Boundary and house rules the reviewer should check

- No value object in `Shared.Contracts`. `OverlayElementV3` is primitives and nullable
  primitives.
- `Ensure.That` (ADR-0105) everywhere except the generated migration.
- `List<OverlayElement> elements = [];`, a collection expression with an explicit type.
- Handlers reading two or more fields destructure first.
- No `default` arm in a kind switch that returns a value; an unhandled kind throws.
- ADR-0084: `OverlayConfiguration` ≤ 300 lines or split; `ParseElement` ≤ 30 lines.
- Coverage: Domain ≥ 90%, Application ≥ 80%.
- Re-grep for `Label` identifiers in `src/OverlayDesigner` after the rename (`grep -rn
  "\bLabel\b" src/OverlayDesigner --include=*.cs | grep -v obj/`). A half-renamed domain is
  worse than either name.
