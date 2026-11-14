# Quickstart: verifying a stage

For each stage's verification note. Everything except the CI run is **small on purpose**: a full stack has exhausted this machine's memory twice.

## Without a stack (do these first, every stage)

1. **The guard**: `pnpm test:guards` (or `node --test scripts/e2e-origins.test.mjs`). Stage 1: first red on today's tree, quote the `file:line`
   list; after the change, green. Every stage: the `MIGRATION` entries for that stage are deleted first, the test goes red naming exactly those
   references, then the references move and it goes green.
2. **Which spec runs where is unchanged**: for each project, before and after the stage,
   `pnpm exec playwright test --list --project=<chromium|seed|kiosk|wall|cleanup>` saved to a file; the diff must be empty (FR-008, SC-002).
3. **The warm-up**: `node --test scripts/warm-one-origin.test.mjs` (stub server: red with a 404 path, naming it; green with three 200s).
4. **Static checks**: `prettier --check`, `eslint --max-warnings 0` on `e2e` and `playwright.config.ts`, `tsc -p e2e/tsconfig.json --noEmit`,
   `pnpm test:guards`.
5. **Stage 2 only, the premise**: `node -e "console.log(new URL('/', 'http://localhost:5180/kiosk/').href)"` prints `http://localhost:5180/`: a
   leading `/` replaces the path.

## With a stack (small, one worker, stack stopped between runs)

6. Boot fresh: `dotnet run --project src/AppHost -c Release --no-build -- ScenarioSimulator=false PersistentStack=false`, then
   `scripts/wait-for-e2e-stack.sh`. Stop it before building (a running stack locks the DLLs); one stack per machine.
7. Run one or two specs of the stage's origin, `--workers=1`, e.g. stage 1 `e2e/cameras.spec.ts`; stage 2 `e2e/kiosk-shows-a-wall.spec.ts`; stage 3
   `e2e/wall-outlives-its-session.spec.ts`. Stage 2's red: the same kiosk spec against the one origin with `goto('/')` still unchanged, one worker.
8. If memory goes critical, stop and say what was not run. Do not retry.

## CI (the proof)

9. Open the PR; read **all** check buckets; for the e2e shards read the retry summary and compare the retried count with `develop`'s last green
   run (SC-005); confirm no spec was dropped (`--list` totals per shard match the baseline).
