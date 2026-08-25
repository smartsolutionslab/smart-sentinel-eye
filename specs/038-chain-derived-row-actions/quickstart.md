# Quickstart: reaching every chain shape by hand

**Feature**: `038-chain-derived-row-actions` · **Plan**: [plan.md](./plan.md)

How to walk a single layout through all eight shapes, and what the verification
note on the PR must contain.

---

## Boot

```sh
dotnet run --project src/AppHost
bash scripts/wait-for-e2e-stack.sh
```

`management-web` serves on <http://localhost:5173>.

---

## The walk — one layout, eight shapes

Create a 2×2 layout named `shapes-demo` with at least two tiles and one overlay
binding. Each step below changes the shape; check the row against
[contracts/row-actions.md](./contracts/row-actions.md) before moving on.

| # | Do this | Shape | Row should offer | Badge |
|---|---|---|---|---|
| 1 | *(just created)* | `{D}` | Publish, Discard draft | `v1 · Draft` |
| 2 | Publish | `{P}` | Edit, Revert, Archive | `v1 · Published` |
| 3 | Edit (new draft) | `{P, D}` | all five | `v1 · Published · draft v2` |
| 4 | Discard draft | `{P, A}` | Edit, Revert, Archive | `v1 · Published` |
| 5 | Edit (new draft), then Revert | `{D, D}` | Publish, Discard draft | `v3 · Draft` |
| 6 | Publish | `{A, P, D}` ≡ `{P, D}` | all five | `v3 · Published · draft v1` |

**Step 4 is the filed defect.** Before this feature that row offered **nothing**
while the layout was live on kiosks.

**Step 5 is the shape the spec did not know about.** Two open drafts and nothing
published, two clicks from a published chain, both clicks offered by the row.

**Step 6's badge is worth pausing on**: the live revision is `v3` and the open
draft is `v1`, because reverting turned the original published revision back into
a draft. The row must name the live one, not the newest.

Repeat the whole walk for an overlay. Its Edit branches without opening a
designer; everything else is identical.

---

## The two confirmations, side by side

On a chain at step 3 (`{P, D}`), open both and read them:

- **Archive** must say the layout goes out of service and that kiosks are sent
  away. It targets `v1`.
- **Discard draft** must say the draft is thrown away and that the layout stays
  as it is. It must **not** say *out of service*, must **not** mention kiosks,
  and must **not** offer to bring anything back. It targets `v2`.

If those two read alike, the feature has not landed — that is what it exists to
separate.

---

## Verification note for the PR

State each with what was observed, not what was expected:

- **All eight shapes offer at least one action**, demonstrated shape by shape
  rather than in aggregate (SC-001). The table above is the walk; say which were
  checked in the app and which by test.
- **Every action's target, by revision number** (SC-003). Archive and Discard
  asserted **on the same chain**, so a swap fails rather than passing twice.
- **`pnpm typecheck && pnpm lint && pnpm test`** clean, with counts.
- **Playwright** run, with its count. Note explicitly that
  `e2e/layouts.spec.ts:56` and `:130` read a row's state text and still match —
  both are on shape `{P}`, whose badge is unchanged.
- **No service change** (FR-013, SC-006): `git diff` over `src/` must be
  **empty**. Show it. If anything under `src/` changed, that is a finding to
  raise, not a fix to keep.
- **The two rewritten tests** (research §7): say what each asserts now, and that
  the substantive claim — editing a `{P, A}` chain opens from the **published**
  revision — survived rather than being dropped with the button-absence check.
- **The deliberate break.** Swap the Archive and Discard targets so each sends the
  other's revision number. Record which assertions go red and how many; both
  requests still succeed, so if fewer than both pages' target assertions fail,
  the targets are not really being checked. Then revert. An assertion that has
  never failed is a claim, not a check.
- **Both twins** (SC-007): every behavioural claim above, demonstrated for the
  layout row and the overlay row.
