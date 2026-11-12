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
