# Verification — spec 488 (#3239)

Behaviour-preserving → **characterisation** (ADR-0139 / ADR-0144 phase 4a). The diff against `origin/develop` is
`apps/management-web/package.json`, `apps/management-cameras/package.json` and `pnpm-lock.yaml` (one line, one line, 25 lines): **no
source, test or snapshot file changed**. Observed under jsdom in vitest, not in a browser; the evidence is two identical suite runs and a
per-form resolver-bypass probe.

## §IV legs touched: none

The seven forms are operator forms in `management-web` and `management-cameras`. No media, event, overlay-state or render-loop code is
involved. `@microsoft/signalr` was deliberately left untouched (owner instruction, 2026-11-10).

## SC-001 — identical counts before and after

The local `node_modules` was stale (`@hookform/resolvers` 5.9.1 against a 6.0.0 manifest), so the baseline was taken after
`pnpm install --frozen-lockfile`, and the installed version was **read from `node_modules`**, not from the manifest.

| Tree                        | Installed resolvers | `management-web`          | `management-cameras`     |
| --------------------------- | ------------------- | ------------------------- | ------------------------ |
| Baseline (unmodified)       | 6.0.0 (both apps)   | 102 files, **740** passed | 24 files, **151** passed |
| Baseline, second web run    | —                   | 102 files, 740 passed     | —                        |
| After the bump (`9405ff7c`) | 7.0.5 (both apps)   | 102 files, **740** passed | 24 files, **151** passed |

Counts are identical; no test file is in the diff.

## SC-002 / FR-006 — the lockfile diff

`git diff --stat` is exactly the three files. Every changed lockfile line is either an `@hookform/resolvers` key (`specifier`, `version`,
the package and snapshot entries) or the new package's own peer metadata: `typebox ^1.0.0` replaces `@sinclair/typebox` and
`@typeschema/main`, and `ajv-formats` widens to `^2.1.1 || ^3.0.0`. All of those are optional peers we do not use. The `zod@` and
`react-hook-form@` snapshot counts are unchanged (4 and 2, before and after). pnpm moved no other package.

## FR-005 — gates (exit codes read)

| Check                                               | Baseline | After                         |
| --------------------------------------------------- | -------- | ----------------------------- |
| `eslint src --max-warnings 0`, `management-web`     | 0        | 0                             |
| `tsc --noEmit`, `management-web`                    | 0        | 0                             |
| `eslint src --max-warnings 0`, `management-cameras` | 0        | 0                             |
| `tsc --noEmit`, `management-cameras`                | 0        | 0                             |
| `prettier --check` on the two manifests             | —        | 0                             |
| `Architecture.Tests` (foreground)                   | —        | 3254 / 3254 passed (4 m 50 s) |

The `eslint-plugin-react` unmet-peer warning (`eslint@10`) printed by pnpm is in the `develop` lockfile already and is not part of this
change.

## FR-007 / SC-003 — does each form's suite reach the resolver?

Probe: `zodResolver(<schema>)` replaced, **exactly once** per form, by `async (values) => ({ values, errors: {} })`, then that form's own
tests run, on the bumped tree, one vitest at a time. The runner restores only the file it broke, from git, and refuses to start on a
dirty tree. The tree was clean after every run. A form is "reached" if at least one test fails with the bypass.

| Form                                               | Tests run | Failed with bypass | Verdict         | Example failing test                                                   |
| -------------------------------------------------- | --------- | ------------------ | --------------- | ---------------------------------------------------------------------- |
| `EditCameraAddressDialog`                          | 18        | 1                  | reached         | "Refuses an unusable address without sending anything"                 |
| `RegisterCameraDialog`                             | 10        | **0**              | **not reached** | —                                                                      |
| `RenameCameraDialog`                               | 15        | **0**              | **not reached** | —                                                                      |
| `OverlayDraftForm` (`useOverlayDraftFormState.ts`) | 122       | 3                  | reached         | "Surfaces a validation error when the name is blank"                   |
| `RuleDialog`                                       | 30        | 8                  | reached         | "Rejects a name that is not lowercase kebab-case"                      |
| `SystemVariableDialog`                             | 15        | 4                  | reached         | "Surfaces a validation error when the name does not match the grammar" |
| `WallForm`                                         | 4         | 2                  | reached         | "Refuses to submit a blank name"                                       |

**Two forms are not reached.** `RegisterCameraDialog`'s "choose which fab" test stays green under the bypass because that refusal is a
hand-written check in the component (`mustChooseFab && fabId === ''`), not the resolver. A repo-wide search for
`registerCameraSchema|renameCameraSchema` finds only the two components, `cameras.api.ts` and `cameras.schema.ts` itself, so the schema
rules (name required/trimmed/200 chars, RTSP URL prefix, length, and the refusal of credentials in the URL, plus the rename form's
documented no-case-normalisation rule) have **no test**.

Per the phase-4a colour a gap is recorded and filed, not fixed in a behaviour-preserving change: **#3241**. It bears on this bump only
as an honest limit: for those two forms the suites cannot show that 7.0.5 behaves like 6.0.0, because no test submits an invalid value.
The mitigation is the release notes and peers (the zod resolver was not touched in 7.0.x) and the five forms where the wiring is
observed, including `EditCameraAddressDialog`, which uses `registerCameraSchema.pick({ rtspUrl })`.

## Follow-up worth recording

#3241 should land its tests against `zodResolver`-bypass probes, on `develop`, after this PR; nothing here depends on it.

## Code review (T011)

Not yet run.
