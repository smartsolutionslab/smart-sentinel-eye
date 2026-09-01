# Data model — 055 find a camera by name

Phase 1. **No schema change and no migration.** Everything matching needs already
exists in the table.

---

## 1. What the database already provides

| Column | Type | Note |
|---|---|---|
| `name` | `varchar(200)` | as the operator typed it; what is displayed |
| `name_normalized` | `varchar(200)` | **generated always as `upper(name)`, stored** |

And the constraint that gives `name_normalized` its meaning:

```
ux_cameras_fab_name_normalized_active
  UNIQUE, btree (fab, name_normalized) WHERE status <> 'Decommissioned'
```

**Matching reuses this column rather than lowering `name` in the query.** The
point is not to save work: it is that *"matches"* and *"is the same name"* then
mean the same thing. A search normalising differently from the uniqueness rule
would either show two distinct cameras as one match or hide one an operator knows
exists.

**The index does not serve this filter**, and that is expected. A btree on
`(fab, name_normalized)` answers a prefix, not a substring. The filter scans the
fab's rows — 250 at the target scale — and whether that is acceptable is measured,
not assumed (FR-014).

---

## 2. `NameFragment` — the operator's input, not a pattern

Added to `ListCamerasQuery` alongside fabs, sort, order, offset, limit and the
retired flag.

| Property | Rule |
|---|---|
| Presence | optional; absent behaves exactly as today (FR-007) |
| Trimming | leading and trailing whitespace ignored (FR-003) |
| Emptiness | a fragment that is empty after trimming is treated as absent, not as "match nothing" |
| Matching | case-insensitive substring against `name_normalized` (FR-001, FR-002) |
| Accents | **not folded** — see research §3 |
| Length | bounded by the name's own 1–200; a longer fragment cannot match and is not an error |

**Not a pattern language.** An operator types words. Characters with meaning in
the underlying match syntax are matched literally, not interpreted — a name
containing `%` is found by typing `%`, and a fragment of `%` does not match
everything. This is a trust boundary: the fragment is operator input arriving over
HTTP.

---

## 3. What the response means when a fragment is present

`CameraListPageDto` is unchanged in **shape** — `(Items, Count, Offset, Limit)` —
and changed in **meaning**, for exactly one field:

| Field | Without a fragment | With a fragment |
|---|---|---|
| `Items` | the requested page of the fab's cameras | the requested page **of the matches** |
| `Count` | how many the fab has | **how many matched** |
| `Offset`, `Limit` | as requested | as requested |

**`Count` describing the matches is the whole of US2.** It follows from filtering
before the count rather than from anyone maintaining it, because the handler
already counts the same query it pages.

**Deliberately not added**: a second total carrying the unfiltered size. No screen
in the spec needs "11 of 250", and a response with two totals invites a consumer
to compare its items against the wrong one — the failure this feature must avoid
creating.

---

## 4. What the screens hold

- **Fragment**: what the operator has typed, before trimming, so the field shows
  what they typed.
- **Match count**: from the response's `Count`. Announced when it changes (FR-011).
- **Request generation**: enough to discard a response older than one already
  shown (FR-013). An operator typing quickly outruns the network, and a stale
  result replacing a newer one is a list that disagrees with the box above it.

---

## 5. Not modelled here

- **No new entity.** A fragment is a query input; nothing is stored and nothing is
  remembered between visits.
- **No index and no extension**, unless §1's measurement asks for one.
- **No change to fab scoping or retired handling.** Both already filter before the
  count; the fragment joins them and changes neither (FR-008).
