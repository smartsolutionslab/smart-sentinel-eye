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
