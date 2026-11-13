# 494 QA frontend evidence

## Red 1: dot segments in returnPath (before the fix)
```
     × Falls back to the base for a dot segment into the other instance 9ms
     × Falls back to the base for a trailing dot segment 1ms
     × Falls back to the base for an encoded dot segment 1ms
⎯⎯⎯⎯⎯⎯⎯ Failed Tests 3 ⎯⎯⎯⎯⎯⎯⎯
 FAIL  src/app/signinPaths.test.ts > Signing in returns to a path inside this instance only > Falls back to the base for a dot segment into the other instance
Expected: "/kiosk/"
Received: "/kiosk/../wall/x"
 FAIL  src/app/signinPaths.test.ts > Signing in returns to a path inside this instance only > Falls back to the base for a trailing dot segment
Expected: "/kiosk/"
Received: "/kiosk/.."
 FAIL  src/app/signinPaths.test.ts > Signing in returns to a path inside this instance only > Falls back to the base for an encoded dot segment
Expected: "/kiosk/"
Received: "/kiosk/%2e%2e/"
      Tests  3 failed | 19 passed (22)
```

## Red 2: router basename option removed temporarily (routerBase.test.ts)
```
     × Matches the layouts route for a path under the kiosk base 1035ms
⎯⎯⎯⎯⎯⎯⎯ Failed Tests 1 ⎯⎯⎯⎯⎯⎯⎯
AssertionError: expected '/' to be '/kiosk' // Object.is equality
Expected: "/kiosk"
Received: "/"
      Tests  1 failed (1)
```

## Counterfactual 3: auth.ts redirect_uri base hard-coded to '/' (authBase.test.ts)
```
     × Signs a wall display in as the wall client while the callback follows its base 267ms
     × Takes the callback from the base alone, not from the mode flag 7ms
⎯⎯⎯⎯⎯⎯⎯ Failed Tests 2 ⎯⎯⎯⎯⎯⎯⎯
Expected: "http://localhost:3000/wall/oidc/callback"
Received: "http://localhost:3000/oidc/callback"
Expected: "http://localhost:3000/kiosk/oidc/callback"
Received: "http://localhost:3000/oidc/callback"
      Tests  2 failed (2)
```

Each edit was restored by re-editing the line; the tests are green again at the tip.
