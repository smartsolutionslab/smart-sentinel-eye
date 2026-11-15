// The one origin (ADR-0192): the gateway's address, where the console, the kiosk and the wall display are all served. The only place the suite
// spells it; scripts/e2e-origins.test.mjs fails the build for a fixed UI port anywhere else.
export const ONE_ORIGIN = 'http://localhost:5180';

// The kiosk and the wall display are base-aware. A base URL with a path would make `goto('/')` resolve to the root, which is the console,
// so the project base stays the origin and a page of either app is opened with its path.
export const KIOSK_PATH = '/kiosk/';
export const WALL_PATH = '/wall/';
