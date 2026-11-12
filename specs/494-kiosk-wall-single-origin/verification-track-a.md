# Track A verification (frontend), spec 494

## T007 red: kioskBase against a stub returning '/'

```
⎯⎯⎯⎯⎯⎯⎯ Failed Tests 7 ⎯⎯⎯⎯⎯⎯⎯
 FAIL  src/app/kioskBase.test.ts > The kiosk serves from a path chosen by its mode flag > Serves the ordinary kiosk from /kiosk/ for mode undefined
 FAIL  src/app/kioskBase.test.ts > The kiosk serves from a path chosen by its mode flag > Serves the ordinary kiosk from /kiosk/ for mode
 FAIL  src/app/kioskBase.test.ts > The kiosk serves from a path chosen by its mode flag > Serves the ordinary kiosk from /kiosk/ for mode kiosk
 FAIL  src/app/kioskBase.test.ts > The kiosk serves from a path chosen by its mode flag > Serves the ordinary kiosk from /kiosk/ for mode operator
AssertionError: expected '/' to be '/kiosk/' // Object.is equality
Expected: "/kiosk/"
Received: "/"
 FAIL  src/app/kioskBase.test.ts > The kiosk serves from a path chosen by its mode flag > Serves the wall display from /wall/ for mode wall
 FAIL  src/app/kioskBase.test.ts > The kiosk serves from a path chosen by its mode flag > Serves the wall display from /wall/ for mode WALL
 FAIL  src/app/kioskBase.test.ts > The kiosk serves from a path chosen by its mode flag > Serves the wall display from /wall/ for mode Wall
AssertionError: expected '/' to be '/wall/' // Object.is equality
Expected: "/wall/"
Received: "/"
 Test Files  1 failed (1)
      Tests  7 failed (7)
```

## T009 red: signinPaths against a stub (empty redirectUri, returnPath always the base, basename unchanged)

7 of 17 fail; the 10 rejection/fallback cases pass against the stub because it always answers the base (they are guards, they become meaningful once returnPath accepts stored paths).

```
⎯⎯⎯⎯⎯⎯⎯ Failed Tests 7 ⎯⎯⎯⎯⎯⎯⎯
 FAIL  src/app/signinPaths.test.ts > The OIDC callback lands under the instance base > Builds the kiosk callback from the origin and the base
AssertionError: expected '' to be 'http://localhost:5180/kiosk/oidc/call…' // Object.is equality
- Expected
+ Received
 FAIL  src/app/signinPaths.test.ts > The OIDC callback lands under the instance base > Builds the wall callback from the origin and the base
AssertionError: expected '' to be 'http://localhost:5180/wall/oidc/callb…' // Object.is equality
- Expected
+ Received
 FAIL  src/app/signinPaths.test.ts > Signing in returns to a path inside this instance only > Returns the stored path when it starts with the base
AssertionError: expected '/kiosk/' to be '/kiosk/layouts/abc' // Object.is equality
Expected: "/kiosk/layouts/abc"
Received: "/kiosk/"
 FAIL  src/app/signinPaths.test.ts > Signing in returns to a path inside this instance only > Keeps the query of the stored path
AssertionError: expected '/kiosk/' to be '/kiosk/layouts/abc?cell=2' // Object.is equality
Expected: "/kiosk/layouts/abc?cell=2"
Received: "/kiosk/"
 FAIL  src/app/signinPaths.test.ts > Signing in returns to a path inside this instance only > Returns a wall path under the wall base
AssertionError: expected '/wall/' to be '/wall/walls/x' // Object.is equality
Expected: "/wall/walls/x"
Received: "/wall/"
 FAIL  src/app/signinPaths.test.ts > The router basename is the base without its trailing slash > Turns /kiosk/ into /kiosk
AssertionError: expected '/kiosk/' to be '/kiosk' // Object.is equality
Expected: "/kiosk"
Received: "/kiosk/"
 FAIL  src/app/signinPaths.test.ts > The router basename is the base without its trailing slash > Turns /wall/ into /wall
AssertionError: expected '/wall/' to be '/wall' // Object.is equality
Expected: "/wall"
Received: "/wall/"
 Test Files  1 failed (1)
      Tests  7 failed | 10 passed (17)
```

## T011 red for /kiosk/, characterisation for /

Passed first (characterisation, today's output): serve with base /, build with bases / and /kiosk/.

```
⎯⎯⎯⎯⎯⎯⎯ Failed Tests 1 ⎯⎯⎯⎯⎯⎯⎯

 FAIL  src/ui/fonts/fontPreload.test.ts > The font preload points where the stylesheet fetches from > Prefixes the dev server base so the preload is not a request outside the instance
AssertionError: expected [ Array(1) ] to deeply equal [ Array(1) ]

- Expected
+ Received

  [
-   "/kiosk/@fs/D:/Github/smart-sentinel-eye/.claude/worktrees/494-track-frontend/apps/shared/src/ui/fonts/IBMPlexSans-Regular-Latin1.woff2",
+   "/@fs/D:/Github/smart-sentinel-eye/.claude/worktrees/494-track-frontend/apps/shared/src/ui/fonts/IBMPlexSans-Regular-Latin1.woff2",
  ]

 ❯ src/ui/fonts/fontPreload.test.ts:23:19
     21|     const hrefs = preloadHrefs({ command: 'serve', base: '/kiosk/', ro…
--
 Test Files  1 failed (1)
      Tests  1 failed | 3 passed (4)
   Start at  23:52:45
   Duration  1.24s (setup 88%, transform 4%, tests 4%, worker 3%, import 2%)

```

## T017 guard (passed first, no red)

T009's file already covers redirectUri for /wall/ and returnPath rejecting /kiosk/x under /wall/. Gap closed by authBase.test.ts (client follows VITE_KIOSK_MODE, callback follows BASE_URL): 2 tests, green on first run. wallMode.test.ts and auth.test.ts are unmodified and green (cited as the characterisation of client and scope).

## Gates (worktree 494-track-frontend)

- kiosk-web vitest: 42 files / 420 tests (baseline 39 / 392 plus 28 new cases in 3 new files). shared vitest: 94 files / 1402 tests (baseline 93 / 1398 plus 4).
- `tsc --noEmit`, `eslint src --max-warnings 0` (exit 0), `prettier --check src`: clean in both apps. `pnpm test:guards`: 350 pass, 0 fail.
- No pre-existing test file edited.

## vite build, both modes (observed built `dist/index.html`)

- `VITE_KIOSK_MODE` unset: favicon, both font preloads, script and stylesheet all under `/kiosk/` (`/kiosk/assets/index-*.js`).
- `VITE_KIOSK_MODE=wall`: all under `/wall/` (`/wall/assets/index-*.js`, `/wall/assets/IBMPlexSans-*.woff2`).
