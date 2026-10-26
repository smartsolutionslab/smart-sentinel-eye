# Plan 463: A colour too bright for the wall

**Spec:** [spec.md](spec.md) · **Issue:** #3108 · **Base:** `develop` at `f848cb39`
**ADRs:** ADR-0190 (decision), ADR-0165, ADR-0146, ADR-0148, ADR-0123, ADR-0067, ADR-0144. **New ADR:** none.

## Slices

| Slice | Story | PR | Engineers | Phase-4a colour |
|---|---|---|---|---|
| **S1** | US1 — invariant, default, migration + record, frontend mirror | one PR | **backend-engineer**, then **frontend-engineer** (disjoint files) | **red**, plus a declared characterisation set and a declared superseded set |
| **S2** | US2 — the editor's colour control | one PR, after S1 merges | **frontend-engineer** only | **red** |

**S1 cannot be split.** Invariant without migration: every existing row (all default labels included) throws on
read through `OverlayColor.From`. Migration without invariant: the API keeps creating `#FFFFFFD9`, and the wall
paints default rows two ways. Backend without the frontend mirror: the console's default is refused with 400 and
create breaks. The three ship together or the system is broken in between.

**One engineer cannot do S1 alone** — the agent briefs split C# and TS. They run in order (backend first, because the
FR-020 pin test reads the backend constants), but own disjoint files, so the frontend engineer may start as soon as
the backend constants (C1) exist.

---

## Bounded context and layers

Overlay Designer only. Domain (C1-C3), Api (C4), Infrastructure (C5-C6); ScenarioSimulator seed (C7); `apps/shared`
+ `apps/management-web` (C8-C10, S2). **No `Shared.Contracts` change** — `OverlayRevisionPublishedV3.Color` stays a
`string`. **No AppHost change.** No cross-context reference; NetArchTest untouched.

## S1 — backend (backend-engineer)

### C1. `Domain/Overlay/RelativeLuminance.cs` (new) and `OverlayColor`

- `public static class RelativeLuminance { public static double Of(OverlayColor color); internal static double
  Of(double r, double g, double b); }` — D1's formula, moved verbatim from `TextLegibility`'s private
  `RelativeLuminance`/`Linearise`. `double` is computed, not persisted (§II exemption ADR-0140, as `TextLegibility`
  already states).
- `TextLegibility` calls it; its private copies are deleted (behaviour-preserving — characterisation set below).
- `OverlayColor`:
  - `public const double MaximumRelativeLuminance = 0.35;`
  - `DefaultValue = "#9F9F9FD9"` (D4).
  - `From` keeps the pattern guard, canonicalises, then refuses `L > MaximumRelativeLuminance` with
    `Ensure`'s argument exception (same mechanism as the pattern guard), message naming the cap and
    `NearestAllowed(...)`'s value.
  - `public static string NearestAllowed(string canonicalHex)` — D3's scaling, on a **string**, because the
    migrator must compute it for a value `From` would refuse. Returns the input unchanged when already compliant.
    Text-alpha raising is **not** here (it needs the element kind) — see C5.
- **Callers affected** (ADR-0190 Consequences asks for the list): `OverlayElementParsing.ParseColor` (Api),
  `ElementRequest` (Api), `OverlayConfiguration` EF conversion (Infrastructure), `TextLegibility.MinimumAlpha`
  (builds candidates from a compliant RGB only — unaffected because alpha does not enter L), `OverlayColor.Default`,
  ScenarioSimulator `OverlayLabel.Color`, and 22 test files (`grep -rln "OverlayColor\.\(From\|Default\)" tests`).

### C2. Invariants (stated so the tests can pin them)

1. `OverlayColor.From(x)` succeeds ⇔ `x` matches the pattern **and** `RelativeLuminance.Of(x) ≤ 0.35`.
2. `RelativeLuminance.Of(OverlayColor.From(NearestAllowed(x))) ≤ 0.35` for every well-formed `x`; chromaticity of
   the result equals `x`'s within 8-bit quantisation; alpha equal.
3. `NearestAllowed(x) == x` when `x` is compliant (idempotent).
4. `OverlayColor.Default` is compliant and legible as text.

### C3. Error surface

Domain throws on construction (as the pattern guard does today); `OverlayElementParsing.ParseColor` already
re-throws `ArgumentException` naming `color` → 400. Extend its message branch so the luminance refusal keeps the
domain's text (cap + nearest), not the "six- or eight-digit hex" text. No new `ApiError` code.

### C4. Api — no new endpoint

`POST /overlays` and the draft `PATCH` already route every colour through `ParseColor`. Nothing else to wire.

### C5. Infrastructure — the migration and its record

- **EF migration** `AddOverlayColourMigrationRecord`: `migrationBuilder.Sql` creating
  `overlay_colour_migrations (revision_id uuid, ordinal int, previous_color varchar(9), migrated_color
  varchar(9), migrated_at timestamptz, PRIMARY KEY (revision_id, ordinal))`. Not mapped in the model (no entity —
  it is a record, read by administrators; A2), so later model diffs do not touch it. The engineer proves the
  migration file exists with `git diff --stat -- src/OverlayDesigner/Infrastructure/Persistence/Migrations/`.
- **`Persistence/OverlayColourCapMigrator.cs`** (new, `IMigrator`, `ContextName => "OverlayDesigner.ColourCap"`),
  modelled on `EventPartitionRolloverMigrator`: in one transaction, `SELECT revision_id, ordinal, kind, color FROM
  overlay_revision_elements FOR UPDATE` (raw SQL — materialising through EF would throw on the rows it exists to
  fix); for each row over the cap compute `OverlayColor.NearestAllowed(color)`, then for `kind = 'Text'` raise alpha
  to `TextLegibility.MinimumAlpha` if illegible; `UPDATE` the row and `INSERT` the record row. Log one
  `[LoggerMessage]` line per changed row (revision, ordinal, previous, migrated) and a count. Idempotent by
  construction (compliant rows are skipped).
- The existing `CHECK (kind <> 'Text' OR substring(color from 8 for 2) >= '75')` still holds: raising alpha only
  increases it.
- **Registration:** `OverlayDesignerPersistenceModule`, immediately after `OverlayDesignerMigrator` (registration
  order is run order, ADR-0067), so it runs after the schema exists and before any Api starts.

### C6. Seeds

`src/ScenarioSimulator/Seeding/OverlayLabel.cs` `Color` → `"#9F9F9FD9"`.

## S1 — frontend mirror (frontend-engineer)

### C7. `apps/shared/src/api/overlays.schema.ts`, `textLegibility.ts`

- `DEFAULT_OVERLAY_COLOR = '#9F9F9FD9'`; `OVERLAY_COLOR_MAX_LUMINANCE = 0.35`.
- `textLegibility.ts`: export `relativeLuminance(hex)` (existing internal computation, exported, not rewritten).
- `colorSchema` gains a refinement: `relativeLuminance(value) <= OVERLAY_COLOR_MAX_LUMINANCE`, message
  "Brighter than the wall allows (relative luminance above 0.35)." Applies to every kind.
- `overlays.schema.test.ts` FR-020 pins gain `OVERLAY_COLOR_MAX_LUMINANCE` ↔ `MaximumRelativeLuminance`.

### C8. `overlayLabelStyle.ts`

Remove the default branch (D5); the default paints its hex with `inkFor`. Update the function's doc comment.

### C9. Literals

`apps/management-web/src/features/overlays/OverlayEditPage.tsx:27` → `DEFAULT_OVERLAY_COLOR`. e2e fixtures over the
cap → their nearest allowed value (superseded set).

## S2 — the editor (frontend-engineer)

### C10. `apps/shared/src/ui/composites/overlayPalette.ts` (new)

`OVERLAY_SWATCHES: readonly { name: string; value: string }[]` — the 13 D7 entries as `#RRGGBBFF` literals (names
are plain strings like `'cyan-700'`, never `var(--…)`, so `DesignTokenLayerTests`' primitive-citation scan is not
engaged).

### C11. `OverlayColorFields.tsx` (new, sibling of `OverlayGeometryFields.tsx`)

Swatch group (Radix `ToggleGroup`-style radio semantics using the existing `ToggleChip` primitive — no new
dependency), a hex `Input` validated by `colorSchema` (RHF + Zod, ADR-0079), and an opacity range input whose
minimum is `minimumTextAlpha(rgb)` for Text and `00` for shapes. Commits one `color` value through the same
`onCommit` path geometry uses, so a colour change is one undo step in `useOverlayEditHistory`. Read-only when the
page is.

### C12. Wiring

`OverlayEditor.tsx` renders `OverlayColorFields` beside `OverlayGeometryFields` for the selected element;
`OverlayDraftForm.tsx` needs no change if the element's `color` already round-trips (it does since spec 300 —
verify, don't assume).

## Messaging

None. No domain event, no integration event (A1).

## Boundary rules

S1: no file under `apps/kiosk-web` changes (it reads `DEFAULT_OVERLAY_COLOR` and the shared style function).
S2: no backend, contract, kiosk, `overlays.schema.ts` or `overlayLabelStyle.ts` file changes —
`git diff --stat origin/develop` in each verification note.

## Phase 4a

### S1 — characterisation, captured green first, must pass unmodified

`TextLegibilityTests`, `TextLegibilityVectorTests`, `OverlayElementConstructionCharacterisationTests`,
`EditDraftRefusalOrderCharacterisationTests`, `textLegibility.test.ts` — they cover the C1 extraction and the
refusal order. Run before any change, quote green.

### S1 — new behaviour, observed red

- Domain: `OverlayColorTests` additions — boundary `#9F9F9F` / `#A0A0A0`, `#FFFFFF10` refused, new default pinned
  and legible; `OverlayColorNearestAllowedTests` (new) — D3's table, invariants C2.2/C2.3 over a fixed grid.
- Architecture: `OverlayColourPaletteTests` (new) — resolves every primitive in `tokens.css` with the existing
  `OklchColorResolver`, and asserts each is either accepted by `OverlayColor.From` or named in an explicit
  `ExcludedPrimitives` list with its reason (over cap / status hue / white). ADR-0190's "every token in the palette".
- Api: a 400 for `#A0A0A0` on POST and on draft PATCH naming `color` and `0.35`.
- Integration (scratch database, spec 317 pattern — `DeadLetterHoldMigrationIntegrationTests`):
  `OverlayColourCapMigrationIntegrationTests` (new) — the four-row scenario, published + archived rows, record rows,
  idempotent second run. **Add the class to a `tests/Integration.Tests/ci-shards/shard-N.filter`.**
- Frontend: schema refinement, FR-020 pin, `overlayLabelStyle.test.ts` default-paints-hex.

### S1 — superseded set (declared; edits limited by rule)

A pre-existing test may be edited **only** to (a) replace a colour literal above the cap with its D3 nearest value
or the new default, or (b) replace the default-token expectation `var(--color-bg-label)` with the default hex. Any
other edit, or any failure not fixed by (a)/(b), is a regression — block, don't adjust. Known members:
`OverlayColorTests` (default literal), `OverlayLabelCharacterisation.test.tsx:73`, `OverlayLabelParity.test.tsx:79`,
`overlayLabelStyle.test.ts:63`, `overlays.schema.test.ts` default pin, `e2e/kiosk-shows-shapes-over-video.spec.ts`
(`#FFEB3B`, `#FFFFFF80`, `#FFA00080`), `e2e/kiosk-paints-a-reordered-overlay.spec.ts`,
`e2e/kiosk-shows-two-labels-over-video.spec.ts`, `e2e/overlay-shapes.spec.ts`, and any of the 22 C# test files
holding a bright literal. The test-writer quotes the full red list after a behaviourless stub; every entry must
map to (a) or (b).

### S2 — new behaviour, observed red

`overlayPalette.test.ts` (13 entries; each passes `colorSchema` and the legibility rule at `FF`; none is a triad
hue), `OverlayColorFields.test.tsx`, `OverlayEditorColour.test.tsx`, `OverlayDraftFormColour.test.tsx`,
`e2e/overlay-colour.spec.ts`. Plus an `OverlayColourPaletteTests` addition: the swatch file's values equal the
accepted, non-status primitives resolved from `tokens.css` (source scan, slash-normalised).

## Counterfactuals (phase 4b, each quoted red)

1. Cap at `0.36` → the `#A0A0A0` refusal test fails.
2. Luminance from `a·L` → `#FFFFFF10` test fails.
3. `Math.Round` instead of floor in `NearestAllowed` → the C2.2 grid finds a result over the cap.
4. Migrator skips text-alpha raising → `#FFFF0099` row reads `#A5A50099` and the integration test fails.
5. Default token branch restored → `overlayLabelStyle` default-paints-hex test fails.
6. (S2) add `#00C853FF` to swatches → palette test fails.

## Latency

See spec §"Latency". S1 phase 5 collects the render-leg records; S2 records "no kiosk file changed" instead.

## Reviewers

S1: backend-reviewer + frontend-reviewer. No security-reviewer: no scope, token or auth change; the validation
tightening is covered by the 400 tests. S2: frontend-reviewer.
